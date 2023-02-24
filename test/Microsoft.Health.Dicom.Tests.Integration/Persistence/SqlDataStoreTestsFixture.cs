// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Numerics;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Dicom.Core.Features.ChangeFeed;
using Microsoft.Health.Dicom.Core.Features.ExtendedQueryTag;
using Microsoft.Health.Dicom.Core.Features.Partition;
using Microsoft.Health.Dicom.Core.Features.Retrieve;
using Microsoft.Health.Dicom.Core.Features.Store;
using Microsoft.Health.Dicom.Core.Features.Workitem;
using Microsoft.Health.Dicom.SqlServer.Features.ChangeFeed;
using Microsoft.Health.Dicom.SqlServer.Features.ExtendedQueryTag;
using Microsoft.Health.Dicom.SqlServer.Features.ExtendedQueryTag.Errors;
using Microsoft.Health.Dicom.SqlServer.Features.Partition;
using Microsoft.Health.Dicom.SqlServer.Features.Retrieve;
using Microsoft.Health.Dicom.SqlServer.Features.Schema;
using Microsoft.Health.Dicom.SqlServer.Features.Store;
using Microsoft.Health.Dicom.SqlServer.Features.Workitem;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Schema.Manager;
using Microsoft.Health.SqlServer.Features.Storage;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Dicom.Tests.Integration.Persistence;

public class SqlDataStoreTestsFixture : IAsyncLifetime
{
    private const string LocalConnectionString = "server=(local);Integrated Security=true;TrustServerCertificate=true";

    private readonly string _masterConnectionString;
    private readonly SchemaInitializer _schemaInitializer;

    internal SqlDataStoreTestsFixture(string databaseName) : this(databaseName, new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max))
    {

    }

    internal SqlDataStoreTestsFixture(string databaseName, SchemaInformation schemaInformation)
    {
        DatabaseName = EnsureArg.IsNotNullOrEmpty(databaseName, nameof(databaseName));
        SchemaInformation = EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));

        IConfiguration environment = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        string initialConnectionString = environment["SqlServer:ConnectionString"] ?? LocalConnectionString;
        _masterConnectionString = new SqlConnectionStringBuilder(initialConnectionString) { InitialCatalog = "master" }.ToString();
        TestConnectionString = new SqlConnectionStringBuilder(initialConnectionString) { InitialCatalog = DatabaseName }.ToString();

        var config = new SqlServerDataStoreConfiguration
        {
            CommandTimeout = TimeSpan.FromSeconds(30),
            ConnectionString = TestConnectionString,
            Initialize = true,
            SchemaOptions = new SqlServerSchemaOptions
            {
                AutomaticUpdatesEnabled = true,
            },
        };

        IOptions<SqlServerDataStoreConfiguration> configOptions = Options.Create(config);

        var scriptProvider = new ScriptProvider<SchemaVersion>();

        var baseScriptProvider = new BaseScriptProvider();

        var mediator = Substitute.For<IMediator>();

        var sqlConnectionStringProvider = new DefaultSqlConnectionStringProvider(configOptions);
        SqlRetryLogicBaseProvider = SqlConfigurableRetryFactory.CreateExponentialRetryProvider(new SqlRetryLogicOption
        {
            NumberOfTries = 5,
            DeltaTime = TimeSpan.FromSeconds(1),
            MaxTimeInterval = TimeSpan.FromSeconds(20),
        });
        var sqlConnectionFactory = new DefaultSqlConnectionBuilder(sqlConnectionStringProvider, SqlRetryLogicBaseProvider);

        SqlTransactionHandler = new SqlTransactionHandler();

        SqlConnectionWrapperFactory = new SqlConnectionWrapperFactory(SqlTransactionHandler, sqlConnectionFactory, SqlRetryLogicBaseProvider, configOptions);

        var schemaManagerDataStore = new SchemaManagerDataStore(SqlConnectionWrapperFactory, configOptions, NullLogger<SchemaManagerDataStore>.Instance);

        SchemaUpgradeRunner = new SchemaUpgradeRunner(scriptProvider, baseScriptProvider, NullLogger<SchemaUpgradeRunner>.Instance, SqlConnectionWrapperFactory, schemaManagerDataStore);

        // TODO: Leverage DI across our XUnit projects
        IServiceProvider _schemaServices = new ServiceCollection()
            .AddSingleton<ISqlConnectionStringProvider>(sqlConnectionStringProvider)
            .AddSingleton(SqlConnectionWrapperFactory)
            .AddSingleton<IReadOnlySchemaManagerDataStore>(schemaManagerDataStore)
            .AddSingleton(SchemaUpgradeRunner)
            .BuildServiceProvider();

        _schemaInitializer = new SchemaInitializer(_schemaServices, configOptions, SchemaInformation, mediator, NullLogger<SchemaInitializer>.Instance);

        var schemaResolver = new PassthroughSchemaVersionResolver(SchemaInformation);

        IndexDataStore = new SqlIndexDataStore(new VersionedCache<ISqlIndexDataStore>(
            schemaResolver,
            new[]
            {
                new SqlIndexDataStoreV1(SqlConnectionWrapperFactory),
                new SqlIndexDataStoreV2(SqlConnectionWrapperFactory),
                new SqlIndexDataStoreV3(SqlConnectionWrapperFactory),
                new SqlIndexDataStoreV4(SqlConnectionWrapperFactory),
                new SqlIndexDataStoreV5(SqlConnectionWrapperFactory),
                new SqlIndexDataStoreV6(SqlConnectionWrapperFactory),
            }),
            NullLogger<SqlIndexDataStore>.Instance);

        InstanceStore = new SqlInstanceStore(new VersionedCache<ISqlInstanceStore>(
            schemaResolver,
            new[]
            {
                new SqlInstanceStoreV1(SqlConnectionWrapperFactory),
                new SqlInstanceStoreV4(SqlConnectionWrapperFactory),
                new SqlInstanceStoreV6(SqlConnectionWrapperFactory),
            }));

        PartitionStore = new SqlPartitionStore(new VersionedCache<ISqlPartitionStore>(
           schemaResolver,
           new[]
           {
                new SqlPartitionStoreV6(SqlConnectionWrapperFactory),
           }));

        ExtendedQueryTagStore = new SqlExtendedQueryTagStore(new VersionedCache<ISqlExtendedQueryTagStore>(
            schemaResolver,
            new[]
            {
                new SqlExtendedQueryTagStoreV1(),
                new SqlExtendedQueryTagStoreV2(SqlConnectionWrapperFactory, NullLogger<SqlExtendedQueryTagStoreV2>.Instance),
                new SqlExtendedQueryTagStoreV4(SqlConnectionWrapperFactory, NullLogger<SqlExtendedQueryTagStoreV4>.Instance),
                new SqlExtendedQueryTagStoreV8(SqlConnectionWrapperFactory, NullLogger<SqlExtendedQueryTagStoreV8>.Instance),
                new SqlExtendedQueryTagStoreV16(SqlConnectionWrapperFactory, NullLogger<SqlExtendedQueryTagStoreV16>.Instance),
            }));

        ExtendedQueryTagErrorStore = new SqlExtendedQueryTagErrorStore(new VersionedCache<ISqlExtendedQueryTagErrorStore>(
           schemaResolver,
           new[]
           {
                new SqlExtendedQueryTagErrorStoreV1(),
                new SqlExtendedQueryTagErrorStoreV4(SqlConnectionWrapperFactory, NullLogger<SqlExtendedQueryTagErrorStoreV4>.Instance),
                new SqlExtendedQueryTagErrorStoreV6(SqlConnectionWrapperFactory, NullLogger<SqlExtendedQueryTagErrorStoreV6>.Instance),
           }));

        IndexWorkitemStore = new SqlWorkitemStore(new VersionedCache<ISqlWorkitemStore>(
            schemaResolver,
            new[]
            {
                new SqlWorkitemStoreV9(SqlConnectionWrapperFactory, NullLogger<SqlWorkitemStoreV9>.Instance),
                new SqlWorkitemStoreV11(SqlConnectionWrapperFactory, NullLogger<SqlWorkitemStoreV11>.Instance),
                new SqlWorkitemStoreV14(SqlConnectionWrapperFactory, NullLogger<SqlWorkitemStoreV14>.Instance)
            }));

        ChangeFeedStore = new SqlChangeFeedStore(new VersionedCache<ISqlChangeFeedStore>(
            schemaResolver,
            new[]
            {
                new SqlChangeFeedStoreV4(SqlConnectionWrapperFactory),
                new SqlChangeFeedStoreV6(SqlConnectionWrapperFactory),
            }));

        IndexDataStoreTestHelper = new SqlIndexDataStoreTestHelper(TestConnectionString);
        ExtendedQueryTagStoreTestHelper = new ExtendedQueryTagStoreTestHelper(TestConnectionString);
        ExtendedQueryTagErrorStoreTestHelper = new ExtendedQueryTagErrorStoreTestHelper(TestConnectionString);
    }

    // Only 1 public constructor is allowed for test fixture.
    public SqlDataStoreTestsFixture()
        : this(GenerateDatabaseName())
    {
    }

    public SqlTransactionHandler SqlTransactionHandler { get; }

    public SqlRetryLogicBaseProvider SqlRetryLogicBaseProvider { get; }

    public SqlConnectionWrapperFactory SqlConnectionWrapperFactory { get; }

    public IIndexDataStore IndexDataStore { get; }

    public IInstanceStore InstanceStore { get; }

    public IPartitionStore PartitionStore { get; }

    public IExtendedQueryTagStore ExtendedQueryTagStore { get; }

    public IExtendedQueryTagErrorStore ExtendedQueryTagErrorStore { get; }

    public IIndexWorkitemStore IndexWorkitemStore { get; }

    public IChangeFeedStore ChangeFeedStore { get; }

    public SchemaUpgradeRunner SchemaUpgradeRunner { get; }
    public string TestConnectionString { get; }

    public IIndexDataStoreTestHelper IndexDataStoreTestHelper { get; }

    public IExtendedQueryTagStoreTestHelper ExtendedQueryTagStoreTestHelper { get; }

    public IExtendedQueryTagErrorStoreTestHelper ExtendedQueryTagErrorStoreTestHelper { get; }

    public SchemaInformation SchemaInformation { get; set; }

    public static string GenerateDatabaseName(string prefix = "DICOMINTEGRATIONTEST_")
    {
        return $"{prefix}{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{BigInteger.Abs(new BigInteger(Guid.NewGuid().ToByteArray()))}";
    }

    public string DatabaseName { get; }

    public async Task InitializeAsync(bool forceIncrementalSchemaUpgrade)
    {
        await CreateDicomDatabaseAsync();
        await VerifyConnectiontoDicomDatabaseAsync();
        // create dicom schema
        await _schemaInitializer.InitializeAsync(forceIncrementalSchemaUpgrade);
    }

    public Task InitializeAsync()
    {
        return InitializeAsync(forceIncrementalSchemaUpgrade: false);
    }

    public async Task DisposeAsync()
    {
        using (var sqlConnection = new SqlConnection(_masterConnectionString))
        {
            await sqlConnection.OpenAsync();
            SqlConnection.ClearAllPools();
            using (SqlCommand sqlCommand = sqlConnection.CreateCommand())
            {
                sqlCommand.CommandTimeout = 600;
                sqlCommand.CommandText = $"DROP DATABASE IF EXISTS {DatabaseName}";
                await sqlCommand.ExecuteNonQueryAsync();
            }
        }
    }

    private async Task CreateDicomDatabaseAsync()
    {
        using var sqlConnection = new SqlConnection(_masterConnectionString);
        sqlConnection.RetryLogicProvider = SqlRetryLogicBaseProvider;
        using SqlCommand command = sqlConnection.CreateCommand();
        await sqlConnection.OpenAsync();
        command.CommandTimeout = 600;
        command.CommandText = $"CREATE DATABASE {DatabaseName}";
        await command.ExecuteNonQueryAsync();
    }

    private async Task VerifyConnectiontoDicomDatabaseAsync()
    {
        using var sqlConnection = new SqlConnection(TestConnectionString);
        sqlConnection.RetryLogicProvider = SqlRetryLogicBaseProvider;
        using SqlCommand sqlCommand = sqlConnection.CreateCommand();
        sqlCommand.RetryLogicProvider = SqlRetryLogicBaseProvider;
        sqlCommand.CommandText = "SELECT 1";
        await sqlConnection.OpenAsync();
        await sqlCommand.ExecuteScalarAsync();
    }
}
