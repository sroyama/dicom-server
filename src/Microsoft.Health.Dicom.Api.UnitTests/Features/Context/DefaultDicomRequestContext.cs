// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Dicom.Core.Features.Context;
using Microsoft.Health.Dicom.Core.Features.Partition;

namespace Microsoft.Health.Dicom.Api.UnitTests.Features.Context;

public class DefaultDicomRequestContext : IDicomRequestContext
{
    public PartitionEntry DataPartitionEntry { get; set; }

    public string StudyInstanceUid { get; set; }

    public string SeriesInstanceUid { get; set; }

    public string SopInstanceUid { get; set; }

    public bool IsTranscodeRequested { get; set; }

    public long BytesTranscoded { get; set; }

    public long ResponseSize { get; set; }

    public int PartCount { get; set; }

    public string Method { get; set; }

    public Uri BaseUri { get; set; }

    public Uri Uri { get; set; }

    public string CorrelationId { get; set; }

    public string RouteName { get; set; }

    public string AuditEventType { get; set; }

    public ClaimsPrincipal Principal { get; set; }

    public IDictionary<string, StringValues> RequestHeaders { get; set; }

    public IDictionary<string, StringValues> ResponseHeaders { get; set; }

    public int? Version { get; set; }
}
