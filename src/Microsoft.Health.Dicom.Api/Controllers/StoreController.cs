// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Threading.Tasks;
using EnsureThat;
using FellowOakDicom;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Dicom.Api.Extensions;
using Microsoft.Health.Dicom.Api.Features.Filters;
using Microsoft.Health.Dicom.Api.Features.Routing;
using Microsoft.Health.Dicom.Core.Extensions;
using Microsoft.Health.Dicom.Core.Features.Audit;
using Microsoft.Health.Dicom.Core.Messages.Store;
using Microsoft.Health.Dicom.Core.Models;
using Microsoft.Health.Dicom.Core.Web;
using DicomAudit = Microsoft.Health.Dicom.Api.Features.Audit;

namespace Microsoft.Health.Dicom.Api.Controllers;

[QueryModelStateValidator]
[ServiceFilter(typeof(DicomAudit.AuditLoggingFilterAttribute))]
[ServiceFilter(typeof(PopulateDataPartitionFilterAttribute))]
public class StoreController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<StoreController> _logger;

    public StoreController(IMediator mediator, ILogger<StoreController> logger)
    {
        EnsureArg.IsNotNull(mediator, nameof(mediator));
        EnsureArg.IsNotNull(logger, nameof(logger));

        _mediator = mediator;
        _logger = logger;
    }

    [AcceptContentFilter(new[] { KnownContentTypes.ApplicationDicomJson })]
    [Produces(KnownContentTypes.ApplicationDicomJson)]
    [Consumes(KnownContentTypes.ApplicationDicom, KnownContentTypes.MultipartRelated)]
    [ProducesResponseType(typeof(DicomDataset), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(DicomDataset), (int)HttpStatusCode.Accepted)]
    [ProducesResponseType((int)HttpStatusCode.NoContent)]
    [ProducesResponseType(typeof(string), (int)HttpStatusCode.BadRequest)]
    [ProducesResponseType(typeof(string), (int)HttpStatusCode.NotAcceptable)]
    [ProducesResponseType(typeof(DicomDataset), (int)HttpStatusCode.Conflict)]
    [ProducesResponseType(typeof(string), (int)HttpStatusCode.UnsupportedMediaType)]
    [HttpPost]
    [VersionedPartitionRoute(KnownRoutes.StoreInstancesRoute, Name = KnownRouteNames.PartitionStoreInstance)]
    [VersionedRoute(KnownRoutes.StoreInstancesRoute, Name = KnownRouteNames.StoreInstance)]
    [AuditEventType(AuditEventSubType.Store)]
    public async Task<IActionResult> PostInstanceAsync()
    {
        return await PostAsync(null);
    }

    [AcceptContentFilter(new[] { KnownContentTypes.ApplicationDicomJson })]
    [Produces(KnownContentTypes.ApplicationDicomJson)]
    [Consumes(KnownContentTypes.ApplicationDicom, KnownContentTypes.MultipartRelated)]
    [ProducesResponseType(typeof(DicomDataset), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(DicomDataset), (int)HttpStatusCode.Accepted)]
    [ProducesResponseType((int)HttpStatusCode.NoContent)]
    [ProducesResponseType(typeof(string), (int)HttpStatusCode.BadRequest)]
    [ProducesResponseType(typeof(string), (int)HttpStatusCode.NotAcceptable)]
    [ProducesResponseType(typeof(DicomDataset), (int)HttpStatusCode.Conflict)]
    [ProducesResponseType(typeof(string), (int)HttpStatusCode.UnsupportedMediaType)]
    [HttpPost]
    [VersionedPartitionRoute(KnownRoutes.StoreInstancesInStudyRoute, Name = KnownRouteNames.PartitionStoreInstancesInStudy)]
    [VersionedRoute(KnownRoutes.StoreInstancesInStudyRoute, Name = KnownRouteNames.StoreInstancesInStudy)]
    [AuditEventType(AuditEventSubType.Store)]
    public async Task<IActionResult> PostInstanceInStudyAsync(string studyInstanceUid)
    {
        return await PostAsync(studyInstanceUid);
    }

    private async Task<IActionResult> PostAsync(string studyInstanceUid)
    {
        _logger.LogInformation("DICOM Web Store Transaction request received, with study instance UID {StudyInstanceUid}", studyInstanceUid);

        StoreResponse storeResponse = await _mediator.StoreDicomResourcesAsync(
            Request.Body,
            Request.ContentType,
            studyInstanceUid,
            HttpContext.RequestAborted);
        if (!string.IsNullOrEmpty(storeResponse.Warning))
        {
            Response.SetWarning(HttpWarningCode.MiscPersistentWarning, Request.GetHost(dicomStandards: true), storeResponse.Warning);
        }

        return StatusCode(
            (int)storeResponse.Status.ToHttpStatusCode(),
            storeResponse.Dataset);
    }
}
