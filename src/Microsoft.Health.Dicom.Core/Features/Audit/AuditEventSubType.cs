﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Dicom.Core.Features.Audit;

/// <summary>
/// Value set defined at http://dicom.nema.org/medical/dicom/current/output/html/part15.html#sect_A.5.1
/// </summary>
public static class AuditEventSubType
{
    public const string System = "http://dicom.nema.org/medical/dicom/current/output/html/part15.html#sect_A.5.1";

    public const string Partition = "partition";

    public const string ChangeFeed = "change-feed";

    public const string Delete = "delete";

    public const string Query = "query";

    public const string Retrieve = "retrieve";

    public const string RetrieveMetadata = "retrieve-metadata";

    public const string Store = "store";

    public const string Export = "export";

    public const string Operation = "operation";

    public const string AddWorkitem = "add-workitem";

    public const string CancelWorkitem = "cancel-workitem";

    public const string QueryWorkitem = "query-workitem";

    public const string RetrieveWorkitem = "retrieve-workitem";

    public const string ChangeStateWorkitem = "change-state-workitem";

    public const string UpdateWorkitem = "update-workitem";

    public const string AddExtendedQueryTag = "add-extended-query-tag";

    public const string RemoveExtendedQueryTag = "remove-extended-query-tag";

    public const string GetAllExtendedQueryTags = "get-all-extended-query-tag";

    public const string GetExtendedQueryTag = "get-extended-query-tag";

    public const string GetExtendedQueryTagErrors = "get-extended-query-tag-errors";

    public const string UpdateExtendedQueryTag = "update-extended-query-tag";

    public const string BulkImportStore = "bulk-import-store";
}
