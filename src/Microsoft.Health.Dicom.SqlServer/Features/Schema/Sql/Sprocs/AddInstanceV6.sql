﻿/*************************************************************
    Stored procedure for adding an instance.
**************************************************************/
--
-- STORED PROCEDURE
--     AddInstanceV6
--
-- FIRST SCHEMA VERSION
--     6
--
-- DESCRIPTION
--     Adds a DICOM instance, now with partition.
--
-- PARAMETERS
--     @partitionKey
--         * The system identified of the data partition.
--     @studyInstanceUid
--         * The study instance UID.
--     @seriesInstanceUid
--         * The series instance UID.
--     @sopInstanceUid
--         * The SOP instance UID.
--     @patientId
--         * The Id of the patient.
--     @patientName
--         * The name of the patient.
--     @referringPhysicianName
--         * The referring physician name.
--     @studyDate
--         * The study date.
--     @studyDescription
--         * The study description.
--     @accessionNumber
--         * The accession number associated for the study.
--     @modality
--         * The modality associated for the series.
--     @performedProcedureStepStartDate
--         * The date when the procedure for the series was performed.
--     @stringExtendedQueryTags
--         * String extended query tag data
--     @longExtendedQueryTags
--         * Long extended query tag data
--     @doubleExtendedQueryTags
--         * Double extended query tag data
--     @dateTimeExtendedQueryTags
--         * DateTime extended query tag data
--     @personNameExtendedQueryTags
--         * PersonName extended query tag data
--     @initialStatus
--         * Initial status of the row
--     @transferSyntaxUid
--         * Instance transfer syntax UID

-- RETURN VALUE
--     The watermark (version).
------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.AddInstanceV6
    @partitionKey                       INT,
    @studyInstanceUid                   VARCHAR(64),
    @seriesInstanceUid                  VARCHAR(64),
    @sopInstanceUid                     VARCHAR(64),
    @patientId                          NVARCHAR(64),
    @patientName                        NVARCHAR(325) = NULL,
    @referringPhysicianName             NVARCHAR(325) = NULL,
    @studyDate                          DATE = NULL,
    @studyDescription                   NVARCHAR(64) = NULL,
    @accessionNumber                    NVARCHAR(64) = NULL,
    @modality                           NVARCHAR(16) = NULL,
    @performedProcedureStepStartDate    DATE = NULL,
    @patientBirthDate                   DATE = NULL,
    @manufacturerModelName              NVARCHAR(64) = NULL,
    @stringExtendedQueryTags dbo.InsertStringExtendedQueryTagTableType_1 READONLY,
    @longExtendedQueryTags dbo.InsertLongExtendedQueryTagTableType_1 READONLY,
    @doubleExtendedQueryTags dbo.InsertDoubleExtendedQueryTagTableType_1 READONLY,
    @dateTimeExtendedQueryTags dbo.InsertDateTimeExtendedQueryTagTableType_2 READONLY,
    @personNameExtendedQueryTags dbo.InsertPersonNameExtendedQueryTagTableType_1 READONLY,
    @initialStatus                      TINYINT,
    @transferSyntaxUid                  VARCHAR(64) = NULL
AS
BEGIN
    SET NOCOUNT ON

    -- We turn off XACT_ABORT so that we can rollback and retry the INSERT/UPDATE into the study table on failure
    SET XACT_ABORT OFF

    -- The transaction is wrapped in a try...catch block in case the INSERT into the study table fails
    BEGIN TRY

        BEGIN TRANSACTION

            DECLARE @currentDate DATETIME2(7) = SYSUTCDATETIME()
            DECLARE @existingStatus TINYINT
            DECLARE @newWatermark BIGINT
            DECLARE @studyKey BIGINT
            DECLARE @seriesKey BIGINT
            DECLARE @instanceKey BIGINT

            SELECT @existingStatus = Status
            FROM dbo.Instance
            WHERE PartitionKey = @partitionKey
                AND StudyInstanceUid = @studyInstanceUid
                AND SeriesInstanceUid = @seriesInstanceUid
                AND SopInstanceUid = @sopInstanceUid

            IF @@ROWCOUNT <> 0
                -- The instance already exists. Set the state = @existingStatus to indicate what state it is in.
                THROW 50409, 'Instance already exists', @existingStatus;

            -- The instance does not exist, insert it.
            SET @newWatermark = NEXT VALUE FOR dbo.WatermarkSequence
            SET @instanceKey = NEXT VALUE FOR dbo.InstanceKeySequence

            -- Insert Study
            -- If we fail to INSERT, we instead must UPDATE the newly added value
            SELECT @studyKey = StudyKey
            FROM dbo.Study WITH(UPDLOCK)
            WHERE PartitionKey = @partitionKey
                AND StudyInstanceUid = @studyInstanceUid

            IF @@ROWCOUNT = 0
            BEGIN TRY

                SET @studyKey = NEXT VALUE FOR dbo.StudyKeySequence

                INSERT INTO dbo.Study
                    (PartitionKey, StudyKey, StudyInstanceUid, PatientId, PatientName, PatientBirthDate, ReferringPhysicianName, StudyDate, StudyDescription, AccessionNumber)
                VALUES
                    (@partitionKey, @studyKey, @studyInstanceUid, @patientId, @patientName, @patientBirthDate, @referringPhysicianName, @studyDate, @studyDescription, @accessionNumber)

            END TRY
            BEGIN CATCH

                 -- While we could obtain a HOLDLOCK on the table, we optimistically obtain an UPDLOCK instead to avoid the range lock on the study table
                IF ERROR_NUMBER() = 2601
                BEGIN

                    SELECT @studyKey = StudyKey
                    FROM dbo.Study WITH(UPDLOCK)
                    WHERE PartitionKey = @partitionKey
                        AND StudyInstanceUid = @studyInstanceUid

                    -- Latest wins
                    UPDATE dbo.Study
                    SET PatientId = @patientId, PatientName = @patientName, PatientBirthDate = @patientBirthDate, ReferringPhysicianName = @referringPhysicianName, StudyDate = @studyDate, StudyDescription = @studyDescription, AccessionNumber = @accessionNumber
                    WHERE PartitionKey = @partitionKey
                        AND StudyKey = @studyKey


                END
                ELSE
                    THROW

            END CATCH
            ELSE
            BEGIN
                -- Latest wins
                UPDATE dbo.Study
                SET PatientId = @patientId, PatientName = @patientName, PatientBirthDate = @patientBirthDate, ReferringPhysicianName = @referringPhysicianName, StudyDate = @studyDate, StudyDescription = @studyDescription, AccessionNumber = @accessionNumber
                WHERE PartitionKey = @partitionKey
                    AND StudyKey = @studyKey
            END

            -- Insert Series
            SELECT @seriesKey = SeriesKey
            FROM dbo.Series WITH(UPDLOCK)
            WHERE StudyKey = @studyKey
            AND SeriesInstanceUid = @seriesInstanceUid
            AND PartitionKey = @partitionKey

            IF @@ROWCOUNT = 0
            BEGIN
                SET @seriesKey = NEXT VALUE FOR dbo.SeriesKeySequence

                INSERT INTO dbo.Series
                    (PartitionKey, StudyKey, SeriesKey, SeriesInstanceUid, Modality, PerformedProcedureStepStartDate, ManufacturerModelName)
                VALUES
                    (@partitionKey, @studyKey, @seriesKey, @seriesInstanceUid, @modality, @performedProcedureStepStartDate, @manufacturerModelName)
            END
            ELSE
            BEGIN
                -- Latest wins
                UPDATE dbo.Series
                SET Modality = @modality, PerformedProcedureStepStartDate = @performedProcedureStepStartDate, ManufacturerModelName = @manufacturerModelName
                WHERE SeriesKey = @seriesKey
                AND StudyKey = @studyKey
                AND PartitionKey = @partitionKey
            END

            -- Insert Instance
            INSERT INTO dbo.Instance
                (PartitionKey, StudyKey, SeriesKey, InstanceKey, StudyInstanceUid, SeriesInstanceUid, SopInstanceUid, Watermark, Status, LastStatusUpdatedDate, CreatedDate, TransferSyntaxUid)
            VALUES
                (@partitionKey, @studyKey, @seriesKey, @instanceKey, @studyInstanceUid, @seriesInstanceUid, @sopInstanceUid, @newWatermark, @initialStatus, @currentDate, @currentDate, @transferSyntaxUid)

            BEGIN TRY

                EXEC dbo.IIndexInstanceCoreV9
                    @partitionKey,
                    @studyKey,
                    @seriesKey,
                    @instanceKey,
                    @newWatermark,
                    @stringExtendedQueryTags,
                    @longExtendedQueryTags,
                    @doubleExtendedQueryTags,
                    @dateTimeExtendedQueryTags,
                    @personNameExtendedQueryTags

            END TRY
            BEGIN CATCH

                THROW

            END CATCH

            SELECT @newWatermark

        COMMIT TRANSACTION

    END TRY
    BEGIN CATCH

        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        THROW

    END CATCH
END
