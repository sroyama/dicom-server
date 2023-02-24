// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Dicom.Core.Configs;
using Microsoft.Health.Dicom.Core.Exceptions;
using Microsoft.Health.Dicom.Core.Extensions;
using Microsoft.Health.Dicom.Core.Features.Common;
using Microsoft.Health.Dicom.Core.Features.Context;
using Microsoft.Health.Dicom.Core.Features.Model;
using Microsoft.Health.Dicom.Core.Messages;
using Microsoft.Health.Dicom.Core.Messages.Retrieve;

namespace Microsoft.Health.Dicom.Core.Features.Retrieve;

public class RetrieveResourceService : IRetrieveResourceService
{
    private readonly IFileStore _blobDataStore;
    private readonly IInstanceStore _instanceStore;
    private readonly ITranscoder _transcoder;
    private readonly IFrameHandler _frameHandler;
    private readonly IAcceptHeaderHandler _acceptHeaderHandler;
    private readonly IDicomRequestContextAccessor _dicomRequestContextAccessor;
    private readonly IMetadataStore _metadataStore;
    private readonly RetrieveConfiguration _retrieveConfiguration;
    private readonly ILogger<RetrieveResourceService> _logger;
    private readonly IInstanceMetadataCache _instanceMetadataCache;
    private readonly IFramesRangeCache _framesRangeCache;

    public RetrieveResourceService(
        IInstanceStore instanceStore,
        IFileStore blobDataStore,
        ITranscoder transcoder,
        IFrameHandler frameHandler,
        IAcceptHeaderHandler acceptHeaderHandler,
        IDicomRequestContextAccessor dicomRequestContextAccessor,
        IMetadataStore metadataStore,
        IInstanceMetadataCache instanceMetadataCache,
        IFramesRangeCache framesRangeCache,
        IOptionsSnapshot<RetrieveConfiguration> retrieveConfiguration,
        ILogger<RetrieveResourceService> logger,
        ILoggerFactory loggerFactory)
    {
        EnsureArg.IsNotNull(instanceStore, nameof(instanceStore));
        EnsureArg.IsNotNull(blobDataStore, nameof(blobDataStore));
        EnsureArg.IsNotNull(transcoder, nameof(transcoder));
        EnsureArg.IsNotNull(frameHandler, nameof(frameHandler));
        EnsureArg.IsNotNull(acceptHeaderHandler, nameof(acceptHeaderHandler));
        EnsureArg.IsNotNull(dicomRequestContextAccessor, nameof(dicomRequestContextAccessor));
        EnsureArg.IsNotNull(metadataStore, nameof(metadataStore));
        EnsureArg.IsNotNull(instanceMetadataCache, nameof(instanceMetadataCache));
        EnsureArg.IsNotNull(framesRangeCache, nameof(framesRangeCache));
        EnsureArg.IsNotNull(logger, nameof(logger));
        EnsureArg.IsNotNull(retrieveConfiguration?.Value, nameof(retrieveConfiguration));

        _instanceStore = instanceStore;
        _blobDataStore = blobDataStore;
        _transcoder = transcoder;
        _frameHandler = frameHandler;
        _acceptHeaderHandler = acceptHeaderHandler;
        _dicomRequestContextAccessor = dicomRequestContextAccessor;
        _metadataStore = metadataStore;
        _retrieveConfiguration = retrieveConfiguration?.Value;
        _logger = logger;
        _instanceMetadataCache = instanceMetadataCache;
        _framesRangeCache = framesRangeCache;
    }

    public async Task<RetrieveResourceResponse> GetInstanceResourceAsync(RetrieveResourceRequest message, CancellationToken cancellationToken)
    {
        EnsureArg.IsNotNull(message, nameof(message));
        var partitionKey = _dicomRequestContextAccessor.RequestContext.GetPartitionKey();

        try
        {
            AcceptHeader validAcceptHeader = _acceptHeaderHandler.GetValidAcceptHeader(
                message.ResourceType,
                message.AcceptHeaders);

            string requestedTransferSyntax = validAcceptHeader.TransferSyntax.Value;
            bool isOriginalTransferSyntaxRequested = DicomTransferSyntaxUids.IsOriginalTransferSyntaxRequested(requestedTransferSyntax);

            if (message.ResourceType == ResourceType.Frames)
            {
                return await GetFrameResourceAsync(
                    message,
                    partitionKey,
                    requestedTransferSyntax,
                    isOriginalTransferSyntaxRequested,
                    validAcceptHeader.MediaType.ToString(),
                    validAcceptHeader.IsSinglePart,
                    cancellationToken);
            }

            // this call throws NotFound when zero instance found
            IEnumerable<InstanceMetadata> retrieveInstances = await _instanceStore.GetInstancesWithProperties(
                message.ResourceType, partitionKey, message.StudyInstanceUid, message.SeriesInstanceUid, message.SopInstanceUid, cancellationToken);
            InstanceMetadata instance = retrieveInstances.First();
            bool needsTranscoding = NeedsTranscoding(isOriginalTransferSyntaxRequested, requestedTransferSyntax, instance);

            _dicomRequestContextAccessor.RequestContext.PartCount = retrieveInstances.Count();

            // we will only support retrieving multiple instance if requested in original format, since we can do lazyStreams
            if (retrieveInstances.Count() > 1 && !isOriginalTransferSyntaxRequested)
            {
                throw new NotAcceptableException(
                    string.Format(CultureInfo.CurrentCulture, DicomCoreResource.RetrieveServiceMultiInstanceTranscodingNotSupported, requestedTransferSyntax));
            }

            // transcoding of single instance
            if (needsTranscoding)
            {
                _logger.LogInformation("Transcoding Instance");
                FileProperties fileProperties = await CheckFileSize(instance, cancellationToken);
                SetTranscodingBillingProperties(fileProperties.ContentLength);

                Stream stream = await _blobDataStore.GetFileAsync(instance.VersionedInstanceIdentifier, cancellationToken);

                IAsyncEnumerable<RetrieveResourceInstance> transcodedStream = GetAsyncEnumerableTranscodedStreams(
                    isOriginalTransferSyntaxRequested,
                    stream,
                    instance,
                    requestedTransferSyntax);

                return new RetrieveResourceResponse(
                    transcodedStream,
                    validAcceptHeader.MediaType.ToString(),
                    validAcceptHeader.IsSinglePart);

            }

            // no transcoding
            IAsyncEnumerable<RetrieveResourceInstance> responses = GetAsyncEnumerableStreams(retrieveInstances, isOriginalTransferSyntaxRequested, requestedTransferSyntax, cancellationToken);
            return new RetrieveResourceResponse(responses, validAcceptHeader.MediaType.ToString(), validAcceptHeader.IsSinglePart);
        }
        catch (DataStoreException e)
        {
            // Log request details associated with exception. Note that the details are not for the store call that failed but for the request only.
            _logger.LogError(e, "Error retrieving dicom resource. StudyInstanceUid: {StudyInstanceUid} SeriesInstanceUid: {SeriesInstanceUid} SopInstanceUid: {SopInstanceUid}", message.StudyInstanceUid, message.SeriesInstanceUid, message.SopInstanceUid);

            throw;
        }
    }

    private async Task<RetrieveResourceResponse> GetFrameResourceAsync(
        RetrieveResourceRequest message,
        int partitionKey,
        string requestedTransferSyntax,
        bool isOriginalTransferSyntaxRequested,
        string mediaType,
        bool isSinglePart,
        CancellationToken cancellationToken)
    {

        if (isSinglePart && message.Frames.Count > 1)
        {
            throw new BadRequestException(DicomCoreResource.SinglePartSupportedForSingleFrame);
        }

        _dicomRequestContextAccessor.RequestContext.PartCount = message.Frames.Count;

        // only caching frames which are required to provide all 3 UIDs and more immutable
        InstanceIdentifier instanceIdentifier = new InstanceIdentifier(message.StudyInstanceUid, message.SeriesInstanceUid, message.SopInstanceUid, partitionKey);
        string key = GenerateInstanceCacheKey(instanceIdentifier);
        InstanceMetadata instance = await _instanceMetadataCache.GetAsync(
            key,
            instanceIdentifier,
            GetInstanceMetadata,
            cancellationToken);

        bool needsTranscoding = NeedsTranscoding(isOriginalTransferSyntaxRequested, requestedTransferSyntax, instance);

        // need the entire DicomDataset for transcoding
        if (!needsTranscoding && instance.InstanceProperties.HasFrameMetadata)
        {
            _logger.LogInformation("Executing fast frame get.");

            // get frame range
            IReadOnlyDictionary<int, FrameRange> framesRange = await _framesRangeCache.GetAsync(
                instance.VersionedInstanceIdentifier.Version,
                instance.VersionedInstanceIdentifier,
                _metadataStore.GetInstanceFramesRangeAsync,
                cancellationToken);

            string responseTransferSyntax = GetResponseTransferSyntax(isOriginalTransferSyntaxRequested, requestedTransferSyntax, instance);

            IAsyncEnumerable<RetrieveResourceInstance> fastFrames = GetAsyncEnumerableFastFrameStreams(
                instance.VersionedInstanceIdentifier,
                framesRange,
                message.Frames,
                responseTransferSyntax,
                cancellationToken);

            return new RetrieveResourceResponse(fastFrames, mediaType, isSinglePart);
        }

        _logger.LogInformation("Downloading the entire instance for frame parsing");
        FileProperties fileProperties = await CheckFileSize(instance, cancellationToken);

        // eagerly doing getFrames to validate frame numbers are valid before returning a response
        Stream stream = await _blobDataStore.GetFileAsync(instance.VersionedInstanceIdentifier, cancellationToken);
        IReadOnlyCollection<Stream> frameStreams = await _frameHandler.GetFramesResourceAsync(
            stream,
            message.Frames,
            isOriginalTransferSyntaxRequested,
            requestedTransferSyntax);

        if (needsTranscoding)
        {
            SetTranscodingBillingProperties(frameStreams.Sum(f => f.Length));
        }

        IAsyncEnumerable<RetrieveResourceInstance> frames = GetAsyncEnumerableFrameStreams(
            frameStreams,
            instance,
            isOriginalTransferSyntaxRequested,
            requestedTransferSyntax);

        return new RetrieveResourceResponse(frames, mediaType, isSinglePart);

    }

    private void SetTranscodingBillingProperties(long bytesTranscoded)
    {
        _dicomRequestContextAccessor.RequestContext.IsTranscodeRequested = true;
        _dicomRequestContextAccessor.RequestContext.BytesTranscoded = bytesTranscoded;
    }

    private async Task<FileProperties> CheckFileSize(InstanceMetadata instance, CancellationToken cancellationToken)
    {
        FileProperties fileProperties = await _blobDataStore.GetFilePropertiesAsync(instance.VersionedInstanceIdentifier, cancellationToken);

        // limit the file size that can be read in memory
        if (fileProperties.ContentLength > _retrieveConfiguration.MaxDicomFileSize)
        {
            throw new NotAcceptableException(string.Format(CultureInfo.CurrentCulture, DicomCoreResource.RetrieveServiceFileTooBig, _retrieveConfiguration.MaxDicomFileSize));
        }

        return fileProperties;
    }

    private static string GetResponseTransferSyntax(bool isOriginalTransferSyntaxRequested, string requestedTransferSyntax, InstanceMetadata instanceMetadata)
    {
        if (isOriginalTransferSyntaxRequested)
        {
            return GetOriginalTransferSyntaxWithBackCompat(requestedTransferSyntax, instanceMetadata);
        }
        return requestedTransferSyntax;
    }

    /// <summary>
    /// Existing dicom files(as of Feb 2022) do not have transferSyntax stored.
    /// Untill we backfill those files, we need this existing buggy fall back code: requestedTransferSyntax can be "*" which is the wrong content-type to return
    /// </summary>
    /// <param name="requestedTransferSyntax"></param>
    /// <param name="instanceMetadata"></param>
    /// <returns></returns>
    private static string GetOriginalTransferSyntaxWithBackCompat(string requestedTransferSyntax, InstanceMetadata instanceMetadata)
    {
        return string.IsNullOrEmpty(instanceMetadata.InstanceProperties.TransferSyntaxUid) ? requestedTransferSyntax : instanceMetadata.InstanceProperties.TransferSyntaxUid;
    }

    private static bool NeedsTranscoding(bool isOriginalTransferSyntaxRequested, string requestedTransferSyntax, InstanceMetadata instanceMetadata)
    {
        if (isOriginalTransferSyntaxRequested)
            return false;

        return !(instanceMetadata.InstanceProperties.TransferSyntaxUid != null
                && DicomTransferSyntaxUids.AreEqual(requestedTransferSyntax, instanceMetadata.InstanceProperties.TransferSyntaxUid));
    }

    private async IAsyncEnumerable<RetrieveResourceInstance> GetAsyncEnumerableStreams(
        IEnumerable<InstanceMetadata> instanceMetadatas,
        bool isOriginalTransferSyntaxRequested,
        string requestedTransferSyntax,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var instanceMetadata in instanceMetadatas)
        {
            FileProperties fileProperties = await _blobDataStore.GetFilePropertiesAsync(instanceMetadata.VersionedInstanceIdentifier, cancellationToken);
            Stream stream = await _blobDataStore.GetStreamingFileAsync(instanceMetadata.VersionedInstanceIdentifier, cancellationToken);
            yield return
                new RetrieveResourceInstance(
                    stream,
                    GetResponseTransferSyntax(isOriginalTransferSyntaxRequested, requestedTransferSyntax, instanceMetadata),
                    fileProperties.ContentLength);
        }
    }

    private static async IAsyncEnumerable<RetrieveResourceInstance> GetAsyncEnumerableFrameStreams(
        IEnumerable<Stream> frameStreams,
        InstanceMetadata instanceMetadata,
        bool isOriginalTransferSyntaxRequested,
        string requestedTransferSyntax)
    {
        // fake await to return AsyncEnumerable and keep the response consistent
        await Task.Run(() => 1);
        // responseTransferSyntax is same for all frames in a instance
        var responseTransferSyntax = GetResponseTransferSyntax(isOriginalTransferSyntaxRequested, requestedTransferSyntax, instanceMetadata);
        foreach (Stream frameStream in frameStreams)
        {
            yield return
                new RetrieveResourceInstance(frameStream, responseTransferSyntax, frameStream.Length);
        }
    }

    private async IAsyncEnumerable<RetrieveResourceInstance> GetAsyncEnumerableTranscodedStreams(
        bool isOriginalTransferSyntaxRequested,
        Stream stream,
        InstanceMetadata instanceMetadata,
        string requestedTransferSyntax)
    {
        Stream transcodedStream = await _transcoder.TranscodeFileAsync(stream, requestedTransferSyntax);

        yield return new RetrieveResourceInstance(transcodedStream, GetResponseTransferSyntax(isOriginalTransferSyntaxRequested, requestedTransferSyntax, instanceMetadata), transcodedStream.Length);
    }

    private async IAsyncEnumerable<RetrieveResourceInstance> GetAsyncEnumerableFastFrameStreams(
        VersionedInstanceIdentifier identifier,
        IReadOnlyDictionary<int, FrameRange> framesRange,
        IReadOnlyCollection<int> frames,
        string responseTransferSyntax,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // eager validation before yield return
        foreach (int frame in frames)
        {
            if (!framesRange.TryGetValue(frame, out FrameRange newFrameRange))
                throw new FrameNotFoundException();
        }

        foreach (int frame in frames)
        {
            FrameRange frameRange = framesRange[frame];
            Stream frameStream = await _blobDataStore.GetFileFrameAsync(identifier, frameRange, cancellationToken);

            yield return new RetrieveResourceInstance(frameStream, responseTransferSyntax, frameRange.Length);
        }
    }

    private static string GenerateInstanceCacheKey(InstanceIdentifier instanceIdentifier)
    {
        return $"{instanceIdentifier.PartitionKey}/{instanceIdentifier.StudyInstanceUid}/{instanceIdentifier.SeriesInstanceUid}/{instanceIdentifier.SopInstanceUid}";
    }

    private async Task<InstanceMetadata> GetInstanceMetadata(InstanceIdentifier instanceIdentifier, CancellationToken cancellationToken)
    {
        IEnumerable<InstanceMetadata> retrieveInstances = await _instanceStore.GetInstancesWithProperties(
                ResourceType.Instance,
                instanceIdentifier.PartitionKey,
                instanceIdentifier.StudyInstanceUid,
                instanceIdentifier.SeriesInstanceUid,
                instanceIdentifier.SopInstanceUid,
                cancellationToken);

        if (!retrieveInstances.Any())
        {
            throw new InstanceNotFoundException();
        }

        return retrieveInstances.First();
    }
}
