// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FellowOakDicom;
using Microsoft.Extensions.Options;
using Microsoft.Health.Dicom.Core.Configs;
using Microsoft.Health.Dicom.Core.Exceptions;
using Microsoft.Health.Dicom.Core.Features.Common;
using Microsoft.Health.Dicom.Core.Features.Context;
using Microsoft.Health.Dicom.Core.Features.Model;
using Microsoft.Health.Dicom.Core.Features.Partition;
using Microsoft.Health.Dicom.Core.Features.Retrieve;
using Microsoft.Health.Dicom.Core.Features.Telemetry;
using Microsoft.Health.Dicom.Core.Messages;
using Microsoft.Health.Dicom.Core.Messages.Retrieve;
using Microsoft.Health.Dicom.Tests.Common;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.Health.Dicom.Core.UnitTests.Features.Retrieve;

public class RetrieveMetadataServiceTests
{
    private readonly IInstanceStore _instanceStore;
    private readonly IMetadataStore _metadataStore;
    private readonly IETagGenerator _eTagGenerator;
    private readonly RetrieveMetadataService _retrieveMetadataService;
    private readonly IDicomRequestContextAccessor _dicomRequestContextAccessor;
    private readonly RetrieveMeter _retrieveMeter;

    private readonly string _studyInstanceUid = TestUidGenerator.Generate();
    private readonly string _seriesInstanceUid = TestUidGenerator.Generate();
    private readonly string _sopInstanceUid = TestUidGenerator.Generate();
    private static readonly CancellationToken DefaultCancellationToken = new CancellationTokenSource().Token;

    public RetrieveMetadataServiceTests()
    {
        _instanceStore = Substitute.For<IInstanceStore>();
        _metadataStore = Substitute.For<IMetadataStore>();
        _eTagGenerator = Substitute.For<IETagGenerator>();
        _dicomRequestContextAccessor = Substitute.For<IDicomRequestContextAccessor>();
        _retrieveMeter = new RetrieveMeter();

        _dicomRequestContextAccessor.RequestContext.DataPartitionEntry = PartitionEntry.Default;
        _retrieveMetadataService = new RetrieveMetadataService(
            _instanceStore,
            _metadataStore,
            _eTagGenerator,
            _dicomRequestContextAccessor,
            _retrieveMeter,
            Options.Create(new RetrieveConfiguration()));
    }

    [Fact]
    public async Task GivenRetrieveStudyMetadataRequest_WhenStudyInstanceUidDoesNotExist_ThenDicomInstanceNotFoundExceptionIsThrownAsync()
    {
        string ifNoneMatch = null;
        InstanceNotFoundException exception = await Assert.ThrowsAsync<InstanceNotFoundException>(() => _retrieveMetadataService.RetrieveStudyInstanceMetadataAsync(TestUidGenerator.Generate(), ifNoneMatch, DefaultCancellationToken));
        Assert.Equal("The specified study cannot be found.", exception.Message);
    }

    [Fact]
    public async Task GivenRetrieveSeriesMetadataRequest_WhenStudyAndSeriesInstanceUidDoesNotExist_ThenDicomInstanceNotFoundExceptionIsThrownAsync()
    {
        string ifNoneMatch = null;
        InstanceNotFoundException exception = await Assert.ThrowsAsync<InstanceNotFoundException>(() => _retrieveMetadataService.RetrieveSeriesInstanceMetadataAsync(TestUidGenerator.Generate(), TestUidGenerator.Generate(), ifNoneMatch, DefaultCancellationToken));
        Assert.Equal("The specified series cannot be found.", exception.Message);
    }

    [Fact]
    public async Task GivenRetrieveSeriesMetadataRequest_WhenStudyInstanceUidDoesNotExist_ThenDicomInstanceNotFoundExceptionIsThrownAsync()
    {
        SetupInstanceIdentifiersList(ResourceType.Series);

        string ifNoneMatch = null;
        InstanceNotFoundException exception = await Assert.ThrowsAsync<InstanceNotFoundException>(() => _retrieveMetadataService.RetrieveSeriesInstanceMetadataAsync(TestUidGenerator.Generate(), _seriesInstanceUid, ifNoneMatch, DefaultCancellationToken));
        Assert.Equal("The specified series cannot be found.", exception.Message);
    }

    [Fact]
    public async Task GivenRetrieveSeriesMetadataRequest_WhenSeriesInstanceUidDoesNotExist_ThenDicomInstanceNotFoundExceptionIsThrownAsync()
    {
        SetupInstanceIdentifiersList(ResourceType.Series);

        string ifNoneMatch = null;
        InstanceNotFoundException exception = await Assert.ThrowsAsync<InstanceNotFoundException>(() => _retrieveMetadataService.RetrieveSeriesInstanceMetadataAsync(_studyInstanceUid, TestUidGenerator.Generate(), ifNoneMatch, DefaultCancellationToken));
        Assert.Equal("The specified series cannot be found.", exception.Message);
    }

    [Fact]
    public async Task GivenRetrieveSopInstanceMetadataRequest_WhenStudySeriesAndSopInstanceUidDoesNotExist_ThenDicomInstanceNotFoundExceptionIsThrownAsync()
    {
        string ifNoneMatch = null;
        InstanceNotFoundException exception = await Assert.ThrowsAsync<InstanceNotFoundException>(() => _retrieveMetadataService.RetrieveSopInstanceMetadataAsync(TestUidGenerator.Generate(), TestUidGenerator.Generate(), TestUidGenerator.Generate(), ifNoneMatch, DefaultCancellationToken));
        Assert.Equal("The specified instance cannot be found.", exception.Message);
    }

    [Fact]
    public async Task GivenRetrieveSopInstanceMetadataRequest_WhenStudyAndSeriesDoesNotExist_ThenDicomInstanceNotFoundExceptionIsThrownAsync()
    {
        SetupInstanceIdentifiersList(ResourceType.Instance);

        string ifNoneMatch = null;
        InstanceNotFoundException exception = await Assert.ThrowsAsync<InstanceNotFoundException>(() => _retrieveMetadataService.RetrieveSopInstanceMetadataAsync(TestUidGenerator.Generate(), TestUidGenerator.Generate(), _sopInstanceUid, ifNoneMatch, DefaultCancellationToken));
        Assert.Equal("The specified instance cannot be found.", exception.Message);
    }

    [Fact]
    public async Task GivenRetrieveSopInstanceMetadataRequest_WhenSeriesInstanceUidDoesNotExist_ThenDicomInstanceNotFoundExceptionIsThrownAsync()
    {
        SetupInstanceIdentifiersList(ResourceType.Instance);

        string ifNoneMatch = null;
        InstanceNotFoundException exception = await Assert.ThrowsAsync<InstanceNotFoundException>(() => _retrieveMetadataService.RetrieveSopInstanceMetadataAsync(_studyInstanceUid, TestUidGenerator.Generate(), _sopInstanceUid, ifNoneMatch, DefaultCancellationToken));
        Assert.Equal("The specified instance cannot be found.", exception.Message);
    }

    [Fact]
    public async Task GivenRetrieveInstanceMetadataRequestForStudy_WhenFailsToRetrieveSome_ThenDicomInstanceNotFoundExceptionIsThrownAsync()
    {
        List<VersionedInstanceIdentifier> versionedInstanceIdentifiers = SetupInstanceIdentifiersList(ResourceType.Study);

        _metadataStore.GetInstanceMetadataAsync(versionedInstanceIdentifiers.Last(), Arg.Any<CancellationToken>()).Throws(new InstanceNotFoundException());
        _metadataStore.GetInstanceMetadataAsync(versionedInstanceIdentifiers.First(), Arg.Any<CancellationToken>()).Returns(new DicomDataset());

        string ifNoneMatch = null;
        RetrieveMetadataResponse response = await _retrieveMetadataService.RetrieveStudyInstanceMetadataAsync(_studyInstanceUid, ifNoneMatch, DefaultCancellationToken);
        InstanceNotFoundException exception = await Assert.ThrowsAsync<InstanceNotFoundException>(() => response.ResponseMetadata.ToListAsync().AsTask());
        Assert.Equal("The specified instance cannot be found.", exception.Message);
    }

    [Fact]
    public async Task GivenRetrieveInstanceMetadataRequestForStudy_WhenIsSuccessful_ThenSuccessStatusCodeIsReturnedAsync()
    {
        List<VersionedInstanceIdentifier> versionedInstanceIdentifiers = SetupInstanceIdentifiersList(ResourceType.Study);

        _metadataStore.GetInstanceMetadataAsync(versionedInstanceIdentifiers.First(), DefaultCancellationToken).Returns(new DicomDataset());
        _metadataStore.GetInstanceMetadataAsync(versionedInstanceIdentifiers.Last(), DefaultCancellationToken).Returns(new DicomDataset());

        string ifNoneMatch = null;
        RetrieveMetadataResponse response = await _retrieveMetadataService.RetrieveStudyInstanceMetadataAsync(_studyInstanceUid, ifNoneMatch, DefaultCancellationToken);

        Assert.Equal(await response.ResponseMetadata.CountAsync(), versionedInstanceIdentifiers.Count);
        Assert.Equal(await response.ResponseMetadata.CountAsync(), _dicomRequestContextAccessor.RequestContext.PartCount);
    }

    [Fact]
    public async Task GivenRetrieveInstanceMetadataRequestForSeries_WhenFailsToRetrieveSome_ThenDicomInstanceNotFoundExceptionIsThrownAsync()
    {
        List<VersionedInstanceIdentifier> versionedInstanceIdentifiers = SetupInstanceIdentifiersList(ResourceType.Series);

        _metadataStore.GetInstanceMetadataAsync(versionedInstanceIdentifiers.Last(), Arg.Any<CancellationToken>()).Throws(new InstanceNotFoundException());
        _metadataStore.GetInstanceMetadataAsync(versionedInstanceIdentifiers.First(), Arg.Any<CancellationToken>()).Returns(new DicomDataset());

        string ifNoneMatch = null;
        RetrieveMetadataResponse response = await _retrieveMetadataService.RetrieveSeriesInstanceMetadataAsync(_studyInstanceUid, _seriesInstanceUid, ifNoneMatch, DefaultCancellationToken);
        InstanceNotFoundException exception = await Assert.ThrowsAsync<InstanceNotFoundException>(() => response.ResponseMetadata.ToListAsync().AsTask());
        Assert.Equal("The specified instance cannot be found.", exception.Message);
    }

    [Fact]
    public async Task GivenRetrieveInstanceMetadataRequestForSeries_WhenIsSuccessful_ThenSuccessStatusCodeIsReturnedAsync()
    {
        List<VersionedInstanceIdentifier> versionedInstanceIdentifiers = SetupInstanceIdentifiersList(ResourceType.Series);

        _metadataStore.GetInstanceMetadataAsync(versionedInstanceIdentifiers.First(), DefaultCancellationToken).Returns(new DicomDataset());
        _metadataStore.GetInstanceMetadataAsync(versionedInstanceIdentifiers.Last(), DefaultCancellationToken).Returns(new DicomDataset());

        string ifNoneMatch = null;
        RetrieveMetadataResponse response = await _retrieveMetadataService.RetrieveSeriesInstanceMetadataAsync(_studyInstanceUid, _seriesInstanceUid, ifNoneMatch, DefaultCancellationToken);

        Assert.Equal(await response.ResponseMetadata.CountAsync(), versionedInstanceIdentifiers.Count);
        Assert.Equal(await response.ResponseMetadata.CountAsync(), _dicomRequestContextAccessor.RequestContext.PartCount);
    }

    [Fact]
    public async Task GivenRetrieveInstanceMetadataRequestForInstance_WhenFailsToRetrieve_ThenDicomInstanceNotFoundExceptionIsThrownAsync()
    {
        VersionedInstanceIdentifier sopInstanceIdentifier = SetupInstanceIdentifiersList(ResourceType.Instance).First();

        _metadataStore.GetInstanceMetadataAsync(sopInstanceIdentifier, Arg.Any<CancellationToken>()).Throws(new InstanceNotFoundException());

        string ifNoneMatch = null;
        RetrieveMetadataResponse response = await _retrieveMetadataService.RetrieveSopInstanceMetadataAsync(_studyInstanceUid, _seriesInstanceUid, _sopInstanceUid, ifNoneMatch, DefaultCancellationToken);
        InstanceNotFoundException exception = await Assert.ThrowsAsync<InstanceNotFoundException>(() => response.ResponseMetadata.ToListAsync().AsTask());
        Assert.Equal("The specified instance cannot be found.", exception.Message);
    }

    [Fact]
    public async Task GivenRetrieveInstanceMetadataRequestForInstance_WhenIsSuccessful_ThenSuccessStatusCodeIsReturnedAsync()
    {
        VersionedInstanceIdentifier sopInstanceIdentifier = SetupInstanceIdentifiersList(ResourceType.Instance).First();

        _metadataStore.GetInstanceMetadataAsync(sopInstanceIdentifier, DefaultCancellationToken).Returns(new DicomDataset());

        string ifNoneMatch = null;
        RetrieveMetadataResponse response = await _retrieveMetadataService.RetrieveSopInstanceMetadataAsync(_studyInstanceUid, _seriesInstanceUid, _sopInstanceUid, ifNoneMatch, DefaultCancellationToken);

        Assert.Equal(1, await response.ResponseMetadata.CountAsync());
        Assert.Equal(1, _dicomRequestContextAccessor.RequestContext.PartCount);
    }

    private List<VersionedInstanceIdentifier> SetupInstanceIdentifiersList(ResourceType resourceType, int partitionKey = DefaultPartition.Key)
    {
        var dicomInstanceIdentifiersList = new List<VersionedInstanceIdentifier>();

        switch (resourceType)
        {
            case ResourceType.Study:
                dicomInstanceIdentifiersList.Add(new VersionedInstanceIdentifier(_studyInstanceUid, TestUidGenerator.Generate(), TestUidGenerator.Generate(), version: 0));
                dicomInstanceIdentifiersList.Add(new VersionedInstanceIdentifier(_studyInstanceUid, TestUidGenerator.Generate(), TestUidGenerator.Generate(), version: 1));
                _instanceStore.GetInstanceIdentifiersInStudyAsync(partitionKey, _studyInstanceUid, DefaultCancellationToken).Returns(dicomInstanceIdentifiersList);
                break;
            case ResourceType.Series:
                dicomInstanceIdentifiersList.Add(new VersionedInstanceIdentifier(_studyInstanceUid, _seriesInstanceUid, TestUidGenerator.Generate(), version: 0));
                dicomInstanceIdentifiersList.Add(new VersionedInstanceIdentifier(_studyInstanceUid, _seriesInstanceUid, TestUidGenerator.Generate(), version: 1));
                _instanceStore.GetInstanceIdentifiersInSeriesAsync(partitionKey, _studyInstanceUid, _seriesInstanceUid, DefaultCancellationToken).Returns(dicomInstanceIdentifiersList);
                break;
            case ResourceType.Instance:
                dicomInstanceIdentifiersList.Add(new VersionedInstanceIdentifier(_studyInstanceUid, _seriesInstanceUid, _sopInstanceUid, version: 0));
                _instanceStore.GetInstanceIdentifierAsync(partitionKey, _studyInstanceUid, _seriesInstanceUid, _sopInstanceUid, DefaultCancellationToken).Returns(dicomInstanceIdentifiersList);
                break;
        }

        return dicomInstanceIdentifiersList;
    }
}
