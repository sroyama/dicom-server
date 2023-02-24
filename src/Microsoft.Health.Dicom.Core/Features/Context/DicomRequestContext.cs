// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using EnsureThat;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Dicom.Core.Features.Partition;

namespace Microsoft.Health.Dicom.Core.Features.Context;

public class DicomRequestContext : IDicomRequestContext
{
    [Obsolete("Please use the other constructor.")]
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "Legacy constructor.")]
    public DicomRequestContext(
        string method,
        string uriString,
        string baseUriString,
        string correlationId,
        IDictionary<string, StringValues> requestHeaders,
        IDictionary<string, StringValues> responseHeaders)
        : this(method, new Uri(uriString), new Uri(baseUriString), correlationId, requestHeaders, responseHeaders)
    { }

    public DicomRequestContext(
        string method,
        Uri uri,
        Uri baseUri,
        string correlationId,
        IDictionary<string, StringValues> requestHeaders,
        IDictionary<string, StringValues> responseHeaders)
    {
        EnsureArg.IsNotNullOrWhiteSpace(method, nameof(method));
        EnsureArg.IsNotNull(uri, nameof(uri));
        EnsureArg.IsNotNull(baseUri, nameof(baseUri));
        EnsureArg.IsNotNull(responseHeaders, nameof(responseHeaders));

        Method = method;
        Uri = uri;
        BaseUri = baseUri;
        CorrelationId = correlationId;
        RequestHeaders = requestHeaders;
        ResponseHeaders = responseHeaders;
        DataPartitionEntry = PartitionEntry.Default;
    }

    public string Method { get; }

    public Uri BaseUri { get; }

    public Uri Uri { get; }

    public string CorrelationId { get; }

    public string RouteName { get; set; }

    public string AuditEventType { get; set; }

    public PartitionEntry DataPartitionEntry { get; set; }

    public string StudyInstanceUid { get; set; }

    public string SeriesInstanceUid { get; set; }

    public string SopInstanceUid { get; set; }

    public bool IsTranscodeRequested { get; set; }

    public long BytesTranscoded { get; set; }

    public long ResponseSize { get; set; }

    public int PartCount { get; set; }

    public ClaimsPrincipal Principal { get; set; }

    public int? Version { get; set; }

    public IDictionary<string, StringValues> RequestHeaders { get; }

    public IDictionary<string, StringValues> ResponseHeaders { get; }
}
