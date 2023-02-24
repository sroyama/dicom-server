// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Dicom.Api.Features.Filters;
using Microsoft.Health.Dicom.Api.Features.Routing;
using Microsoft.Health.Dicom.Core.Extensions;
using Microsoft.Health.Dicom.Core.Features.Audit;
using Microsoft.Health.Dicom.Core.Features.ChangeFeed;
using Microsoft.Health.Dicom.Core.Web;
using DicomAudit = Microsoft.Health.Dicom.Api.Features.Audit;

namespace Microsoft.Health.Dicom.Api.Controllers;

[QueryModelStateValidator]
[ServiceFilter(typeof(DicomAudit.AuditLoggingFilterAttribute))]
public class ChangeFeedController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<ChangeFeedController> _logger;

    public ChangeFeedController(IMediator mediator, ILogger<ChangeFeedController> logger)
    {
        EnsureArg.IsNotNull(mediator, nameof(mediator));
        EnsureArg.IsNotNull(logger, nameof(logger));

        _mediator = mediator;
        _logger = logger;
    }

    [HttpGet]
    [Produces(KnownContentTypes.ApplicationJson)]
    [ProducesResponseType(typeof(IEnumerable<ChangeFeedEntry>), (int)HttpStatusCode.OK)]
    [ProducesResponseType((int)HttpStatusCode.Unauthorized)]
    [ProducesResponseType(typeof(string), (int)HttpStatusCode.BadRequest)]
    [VersionedRoute(KnownRoutes.ChangeFeed)]
    [AuditEventType(AuditEventSubType.ChangeFeed)]
    public async Task<IActionResult> GetChangeFeed([FromQuery] long offset = 0, [FromQuery] int limit = 10, [FromQuery] bool includeMetadata = true)
    {
        _logger.LogInformation("Change feed was read with an offset of {Offset} and limit of {Limit} and metadata is {Metadata} included.", offset, limit, includeMetadata ? string.Empty : "not");

        var response = await _mediator.GetChangeFeed(
            offset,
            limit,
            includeMetadata,
            cancellationToken: HttpContext.RequestAborted);

        return StatusCode((int)HttpStatusCode.OK, response.Entries);
    }

    [HttpGet]
    [Produces(KnownContentTypes.ApplicationJson)]
    [ProducesResponseType(typeof(ChangeFeedEntry), (int)HttpStatusCode.OK)]
    [ProducesResponseType((int)HttpStatusCode.Unauthorized)]
    [ProducesResponseType(typeof(string), (int)HttpStatusCode.BadRequest)]
    [VersionedRoute(KnownRoutes.ChangeFeedLatest)]
    [AuditEventType(AuditEventSubType.ChangeFeed)]
    public async Task<IActionResult> GetChangeFeedLatest([FromQuery] bool includeMetadata = true)
    {
        _logger.LogInformation("Change feed latest was read and metadata is {Metadata} included.", includeMetadata ? string.Empty : "not");

        var response = await _mediator.GetChangeFeedLatest(
            includeMetadata,
            cancellationToken: HttpContext.RequestAborted);

        return StatusCode((int)HttpStatusCode.OK, response.Entry);
    }
}
