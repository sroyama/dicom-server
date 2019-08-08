﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Dicom;
using Microsoft.Health.Dicom.Core.Features.Persistence;
using Microsoft.Health.Dicom.Tests.Common;
using Microsoft.Health.Dicom.Web.Tests.E2E.Clients;
using Microsoft.Net.Http.Headers;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Health.Dicom.Web.Tests.E2E.Rest
{
    public class RetrieveTransactionResourceTests : IClassFixture<HttpIntegrationTestFixture<Startup>>
    {
        private readonly ITestOutputHelper output;

        public static readonly List<string> SupportedTransferSyntaxesFor8BitTranscoding = new List<string>
        {
            "DeflatedExplicitVRLittleEndian",
            "ExplicitVRBigEndian",
            "ExplicitVRLittleEndian",
            "ImplicitVRLittleEndian",
            "JPEG2000Lossless",
            "JPEG2000Lossy",
            "JPEGProcess1",
            "JPEGProcess2_4",
            "RLELossless",
        };

        public static readonly List<string> SupportedTransferSyntaxesForOver8BitTranscoding = new List<string>
        {
            "DeflatedExplicitVRLittleEndian",
            "ExplicitVRBigEndian",
            "ExplicitVRLittleEndian",
            "ImplicitVRLittleEndian",
            "RLELossless",
        };

        public RetrieveTransactionResourceTests(HttpIntegrationTestFixture<Startup> fixture, ITestOutputHelper output)
        {
            Client = new DicomWebClient(fixture.HttpClient);
            this.output = output;
        }

        protected DicomWebClient Client { get; set; }

        [Fact]
        public async Task GivenARequestWithFrameLessThanOrEqualTo0_WhenRetrievingFrames_TheServerShouldReturnBadRequest()
        {
            var studyInstanceUID = Guid.NewGuid().ToString();
            var seriesInstanceUID = Guid.NewGuid().ToString();
            var sopInstanceUID = Guid.NewGuid().ToString();

            HttpResult<IReadOnlyList<Stream>> response = await Client.GetFramesAsync(studyInstanceUID, seriesInstanceUID, sopInstanceUID, frames: 0);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            response = await Client.GetFramesAsync(studyInstanceUID, seriesInstanceUID, sopInstanceUID, frames: -1);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            response = await Client.GetFramesAsync(studyInstanceUID, seriesInstanceUID, sopInstanceUID, frames: new[] { 1, 2, -1 });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task GivenARequestWithInvalidIdentifier_WhenRetrieving_TheServerShouldReturnBadRequest()
        {
            var invalidId1 = new string('b', 65);
            var validId1 = Guid.NewGuid().ToString();
            var validId2 = Guid.NewGuid().ToString();

            HttpResult<IReadOnlyList<DicomFile>> response = await Client.GetStudyAsync(studyInstanceUID: invalidId1);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            response = await Client.GetSeriesAsync(studyInstanceUID: validId1, seriesInstanceUID: invalidId1);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            response = await Client.GetSeriesAsync(studyInstanceUID: invalidId1, seriesInstanceUID: validId1);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            response = await Client.GetSeriesAsync(studyInstanceUID: validId2, seriesInstanceUID: validId2);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            response = await Client.GetInstanceAsync(studyInstanceUID: validId1, seriesInstanceUID: validId2, sopInstanceUID: invalidId1);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            response = await Client.GetInstanceAsync(studyInstanceUID: invalidId1, seriesInstanceUID: validId1, sopInstanceUID: validId2);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            response = await Client.GetInstanceAsync(studyInstanceUID: validId1, seriesInstanceUID: invalidId1, sopInstanceUID: validId2);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            response = await Client.GetInstanceAsync(studyInstanceUID: validId2, seriesInstanceUID: invalidId1, sopInstanceUID: validId2);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            HttpResult<IReadOnlyList<Stream>> framesResponse = await Client.GetFramesAsync(studyInstanceUID: validId1, seriesInstanceUID: validId2, sopInstanceUID: invalidId1, frames: 1);
            Assert.Equal(HttpStatusCode.BadRequest, framesResponse.StatusCode);
            framesResponse = await Client.GetFramesAsync(studyInstanceUID: invalidId1, seriesInstanceUID: validId1, sopInstanceUID: validId2, frames: 1);
            Assert.Equal(HttpStatusCode.BadRequest, framesResponse.StatusCode);
            framesResponse = await Client.GetFramesAsync(studyInstanceUID: validId1, seriesInstanceUID: invalidId1, sopInstanceUID: validId2, frames: 1);
            Assert.Equal(HttpStatusCode.BadRequest, framesResponse.StatusCode);
            framesResponse = await Client.GetFramesAsync(studyInstanceUID: validId2, seriesInstanceUID: invalidId1, sopInstanceUID: validId2, frames: 1);
            Assert.Equal(HttpStatusCode.BadRequest, framesResponse.StatusCode);
        }

        [Fact]
        public async Task GivenNonExistentIdentifiers_WhenRetrieving_TheServerReturnsNotFound()
        {
            HttpResult<IReadOnlyList<DicomFile>> response1 = await Client.GetStudyAsync(Guid.NewGuid().ToString());
            Assert.Equal(HttpStatusCode.NotFound, response1.StatusCode);
            HttpResult<IReadOnlyList<DicomFile>> response2 = await Client.GetSeriesAsync(Guid.NewGuid().ToString(), Guid.NewGuid().ToString());
            Assert.Equal(HttpStatusCode.NotFound, response2.StatusCode);
            HttpResult<IReadOnlyList<DicomFile>> response3 = await Client.GetInstanceAsync(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), Guid.NewGuid().ToString());
            Assert.Equal(HttpStatusCode.NotFound, response3.StatusCode);
            HttpResult<IReadOnlyList<Stream>> response4 = await Client.GetFramesAsync(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), frames: 1);
            Assert.Equal(HttpStatusCode.NotFound, response4.StatusCode);

            // Create a valid Study/ Series/ Instance with one frame
            var studyInstanceUID = Guid.NewGuid().ToString();
            var seriesInstanceUID = Guid.NewGuid().ToString();
            var sopInstanceUID = Guid.NewGuid().ToString();
            DicomFile dicomFile1 = Samples.CreateRandomDicomFileWith8BitPixelData(studyInstanceUID, seriesInstanceUID, sopInstanceUID);
            HttpResult<DicomDataset> storeResponse = await Client.PostAsync(new[] { dicomFile1 }, studyInstanceUID);
            ValidationHelpers.ValidateSuccessSequence(storeResponse.Value.GetSequence(DicomTag.ReferencedSOPSequence), dicomFile1.Dataset);

            HttpResult<IReadOnlyList<DicomFile>> response5 = await Client.GetSeriesAsync(studyInstanceUID, Guid.NewGuid().ToString());
            Assert.Equal(HttpStatusCode.NotFound, response5.StatusCode);
            HttpResult<IReadOnlyList<DicomFile>> response6 = await Client.GetInstanceAsync(studyInstanceUID, seriesInstanceUID, Guid.NewGuid().ToString());
            Assert.Equal(HttpStatusCode.NotFound, response6.StatusCode);
            HttpResult<IReadOnlyList<Stream>> response7 = await Client.GetFramesAsync(studyInstanceUID, seriesInstanceUID, sopInstanceUID, frames: 1);
            Assert.Equal(HttpStatusCode.NotFound, response7.StatusCode);
            HttpResult<IReadOnlyList<Stream>> response8 = await Client.GetFramesAsync(studyInstanceUID, seriesInstanceUID, sopInstanceUID, frames: 2);
            Assert.Equal(HttpStatusCode.NotFound, response8.StatusCode);
        }

        [Fact]
        public async Task GivenStoredDicomFileWithNoContent_WhenRetrieved_TheFileIsRetrievedCorrectly()
        {
            var studyInstanceUID = Guid.NewGuid().ToString();
            DicomFile dicomFile1 = Samples.CreateRandomDicomFile(studyInstanceUID);
            var dicomInstance = DicomInstance.Create(dicomFile1.Dataset);
            HttpResult<DicomDataset> response = await Client.PostAsync(new[] { dicomFile1 }, studyInstanceUID);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            DicomSequence successSequence = response.Value.GetSequence(DicomTag.ReferencedSOPSequence);
            ValidationHelpers.ValidateSuccessSequence(successSequence, dicomFile1.Dataset);

            string studyRetrieveLocation = response.Value.GetSingleValue<string>(DicomTag.RetrieveURL);
            string instanceRetrieveLocation = successSequence.Items[0].GetSingleValue<string>(DicomTag.RetrieveURL);

            HttpResult<IReadOnlyList<DicomFile>> studyByUrlRetrieve = await Client.GetInstancesAsync(new Uri(studyRetrieveLocation));
            ValidateRetrieveTransaction(studyByUrlRetrieve, HttpStatusCode.OK, DicomTransferSyntax.ExplicitVRLittleEndian, dicomFile1);
            HttpResult<IReadOnlyList<DicomFile>> instanceByUrlRetrieve = await Client.GetInstancesAsync(new Uri(instanceRetrieveLocation));
            ValidateRetrieveTransaction(instanceByUrlRetrieve, HttpStatusCode.OK, DicomTransferSyntax.ExplicitVRLittleEndian, dicomFile1);

            HttpResult<IReadOnlyList<DicomFile>> studyRetrieve = await Client.GetStudyAsync(dicomInstance.StudyInstanceUID);
            ValidateRetrieveTransaction(studyRetrieve, HttpStatusCode.OK, DicomTransferSyntax.ExplicitVRLittleEndian, dicomFile1);
            HttpResult<IReadOnlyList<DicomFile>> seriesRetrieve = await Client.GetSeriesAsync(dicomInstance.StudyInstanceUID, dicomInstance.SeriesInstanceUID);
            ValidateRetrieveTransaction(seriesRetrieve, HttpStatusCode.OK, DicomTransferSyntax.ExplicitVRLittleEndian, dicomFile1);
            HttpResult<IReadOnlyList<DicomFile>> instanceRetrieve = await Client.GetInstanceAsync(dicomInstance.StudyInstanceUID, dicomInstance.SeriesInstanceUID, dicomInstance.SopInstanceUID);
            ValidateRetrieveTransaction(instanceRetrieve, HttpStatusCode.OK, DicomTransferSyntax.ExplicitVRLittleEndian, dicomFile1);
        }

        [Theory]
        [InlineData("1.2.840.10008.1.2.4.100", HttpStatusCode.NotAcceptable)] // Unsupported conversion - a video codec
        [InlineData("Bogus TS", HttpStatusCode.BadRequest)] // A non-existent codec
        [InlineData("1.2.840.10008.5.1.4.1.1.1", HttpStatusCode.BadRequest)] // Valid UID, but not a transfer syntax
        public async Task GivenAnUnsupportedTransferSyntax_WhenRetrievingStudy_NotAcceptableIsReturned(string transferSyntax, HttpStatusCode expectedStatusCode)
        {
            var dicomFiles = Samples.GetDicomFilesForTranscoding();
            var dicomFile = dicomFiles.FirstOrDefault(f => (Path.GetFileNameWithoutExtension(f.File.Name) == "ExplicitVRLittleEndian"));

            HttpResult<DicomDataset> postResponse = await Client.PostAsync(new[] { dicomFile });

            // TODO: fix this and add proper cleanup of posted resources once delete is implemented
            Assert.True((postResponse.StatusCode == HttpStatusCode.OK) || (postResponse.StatusCode == HttpStatusCode.Conflict));

            var studyInstanceUID = dicomFile.Dataset.GetSingleValue<string>(DicomTag.StudyInstanceUID);
            var seriesInstanceUID = dicomFile.Dataset.GetSingleValue<string>(DicomTag.SeriesInstanceUID);
            var sopInstanceUID = dicomFile.Dataset.GetSingleValue<string>(DicomTag.SOPInstanceUID);

            var getResponse = await Client.GetInstanceAsync(studyInstanceUID, seriesInstanceUID, sopInstanceUID, transferSyntax);
            Assert.Equal(expectedStatusCode, getResponse.StatusCode);
        }

        // Test that if no TS specified, we return the original TS w/o transcoding -
        // http://dicom.nema.org/medical/dicom/current/output/html/part18.html#sect_8.7.3.5.2:S
        // The wildcard value "*" indicates that the user agent will accept any Transfer Syntax.
        // This allows, for example, the origin server to respond without needing to encode an
        // existing representation to a new Transfer Syntax, or to respond with the
        // Explicit VR Little Endian Transfer Syntax regardless of the Transfer Syntax stored.
        [Fact]
        public async Task GivenAnUnsupportedTransferSyntax_WhenNoTsSpecified_OriginalImageReturned()
        {
            var seriesInstanceUID = DicomUID.Generate();
            var studyInstanceUID = DicomUID.Generate();

            var dicomFile = Samples.CreateRandomDicomFileWith8BitPixelData(
                studyInstanceUID.UID,
                seriesInstanceUID.UID,
                transferSyntax: DicomTransferSyntax.HEVCH265Main10ProfileLevel51.UID.UID,
                encode: false);

            HttpResult<DicomDataset> postResponse = await Client.PostAsync(new[] { dicomFile });

            // TODO: fix this and add proper cleanup of posted resources once delete is implemented
            Assert.True((postResponse.StatusCode == HttpStatusCode.OK) || (postResponse.StatusCode == HttpStatusCode.Conflict));

            var getResponse = await Client.GetSeriesAsync(
                studyInstanceUID.UID,
                seriesInstanceUID.UID);

            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
            Assert.Equal(DicomTransferSyntax.HEVCH265Main10ProfileLevel51, getResponse.Value.Single().Dataset.InternalTransferSyntax);
        }

        [Fact]
        public async Task GivenAMixOfTransferSyntaxes_WhenSomeAreSupported_PartialIsReturned()
        {
            var seriesInstanceUID = DicomUID.Generate();
            var studyInstanceUID = DicomUID.Generate();

            var dicomFile1 = Samples.CreateRandomDicomFileWith8BitPixelData(
                studyInstanceUID.UID,
                seriesInstanceUID.UID,
                transferSyntax: DicomTransferSyntax.ExplicitVRLittleEndian.UID.UID);

            var dicomFile2 = Samples.CreateRandomDicomFileWith8BitPixelData(
                studyInstanceUID.UID,
                seriesInstanceUID.UID,
                transferSyntax: DicomTransferSyntax.HEVCH265Main10ProfileLevel51.UID.UID,
                encode: false);

            var dicomFile3 = Samples.CreateRandomDicomFileWith8BitPixelData(
                studyInstanceUID.UID,
                seriesInstanceUID.UID,
                transferSyntax: DicomTransferSyntax.ImplicitVRLittleEndian.UID.UID);

            HttpResult<DicomDataset> postResponse = await Client.PostAsync(new[] { dicomFile1, dicomFile2, dicomFile3 });

            // TODO: fix this and add proper cleanup of posted resources once delete is implemented
            Assert.True((postResponse.StatusCode == HttpStatusCode.OK) || (postResponse.StatusCode == HttpStatusCode.Conflict));

            var getResponse = await Client.GetSeriesAsync(
                studyInstanceUID.UID,
                seriesInstanceUID.UID,
                DicomTransferSyntax.JPEG2000Lossy.UID.UID);

            Assert.Equal(HttpStatusCode.PartialContent, getResponse.StatusCode);
            Assert.Equal(2, getResponse.Value.Count);
        }

        public static IEnumerable<object[]> Get8BitTranscoderCombos()
        {
            var fromList = SupportedTransferSyntaxesFor8BitTranscoding;
            var toList = SupportedTransferSyntaxesFor8BitTranscoding;

            return from x in fromList from y in toList select new[] { x, y };
        }

        public static IEnumerable<object[]> Get16BitTranscoderCombos()
        {
            var fromList = SupportedTransferSyntaxesForOver8BitTranscoding;
            var toList = SupportedTransferSyntaxesForOver8BitTranscoding;

            return from x in fromList from y in toList select new[] { x, y };
        }

        [Theory]
        [MemberData(nameof(Get16BitTranscoderCombos))]
        public async Task GivenSupported16bitTransferSyntax_WhenRetrievingStudyAndAskingForConversion_OKIsReturned(
            string tsFrom,
            string tsTo)
        {
            var dicomFile = Samples.CreateRandomDicomFileWith16BitPixelData(transferSyntax: ((DicomTransferSyntax)typeof(DicomTransferSyntax).GetField(tsFrom).GetValue(null)).UID.UID);

            HttpResult<DicomDataset> postResponse = await Client.PostAsync(new[] { dicomFile });

            // TODO: fix this and add proper cleanup of posted resources once delete is implemented
            Assert.True((postResponse.StatusCode == HttpStatusCode.OK) || (postResponse.StatusCode == HttpStatusCode.Conflict));

            var studyInstanceUID = dicomFile.Dataset.GetSingleValue<string>(DicomTag.StudyInstanceUID);
            var seriesInstanceUID = dicomFile.Dataset.GetSingleValue<string>(DicomTag.SeriesInstanceUID);
            var sopInstanceUID = dicomFile.Dataset.GetSingleValue<string>(DicomTag.SOPInstanceUID);
            var expectedTransferSyntax = (DicomTransferSyntax)typeof(DicomTransferSyntax).GetField(tsTo).GetValue(null);

            var getResponse = await Client.GetInstanceAsync(studyInstanceUID, seriesInstanceUID, sopInstanceUID, expectedTransferSyntax.UID.UID);
            Assert.Equal(expectedTransferSyntax, getResponse.Value.Single().Dataset.InternalTransferSyntax);
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        }

        [Theory]
        [MemberData(nameof(Get8BitTranscoderCombos))]
        public async Task GivenSupported8bitTransferSyntax_WhenRetrievingStudyAndAskingForConversion_OKIsReturned(
            string tsFrom,
            string tsTo)
        {
            var dicomFiles = Samples.GetDicomFilesForTranscoding();
            var dicomFile = dicomFiles.FirstOrDefault(f => (Path.GetFileNameWithoutExtension(f.File.Name) == tsFrom));

            HttpResult<DicomDataset> postResponse = await Client.PostAsync(new[] { dicomFile });

            // TODO: fix this and add proper cleanup of posted resources once delete is implemented
            Assert.True((postResponse.StatusCode == HttpStatusCode.OK) || (postResponse.StatusCode == HttpStatusCode.Conflict));

            var studyInstanceUID = dicomFile.Dataset.GetSingleValue<string>(DicomTag.StudyInstanceUID);
            var seriesInstanceUID = dicomFile.Dataset.GetSingleValue<string>(DicomTag.SeriesInstanceUID);
            var sopInstanceUID = dicomFile.Dataset.GetSingleValue<string>(DicomTag.SOPInstanceUID);
            var expectedTransferSyntax = (DicomTransferSyntax)typeof(DicomTransferSyntax).GetField(tsTo).GetValue(null);

            var getResponse = await Client.GetInstanceAsync(studyInstanceUID, seriesInstanceUID, sopInstanceUID, expectedTransferSyntax.UID.UID);

            Assert.Equal(expectedTransferSyntax, getResponse.Value.Single().Dataset.InternalTransferSyntax);
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
            Assert.NotNull(getResponse.Value.Single());
        }

        [Theory]
        [InlineData("1.2.840.10008.1.2.4.91")] // JPEG Process 1 - should work, but doesn't for this particular image. Not officially supported
        public async Task GivenAnExceptionDuringTranscoding_WhenRetrievingStudy_BadRequestIsReturned(string transferSyntax)
        {
            var dicomFiles = Samples.GetSampleDicomFiles();

            // var dicomFile = dicomFiles.First();
            var dicomFile = dicomFiles.FirstOrDefault(f => (Path.GetFileNameWithoutExtension(f.File.Name) == "XRJPEGProcess1"));

            // var dicomFile = Samples.CreateRandomDicomFileWith8BitPixelData(transferSyntax: DicomTransferSyntax.DeflatedExplicitVRLittleEndian.UID.UID,  encode: false);

            HttpResult<DicomDataset> postResponse = await Client.PostAsync(new[] { dicomFile });

            // TODO: fix this and add proper cleanup of posted resources once delete is implemented
            Assert.True((postResponse.StatusCode == HttpStatusCode.OK) || (postResponse.StatusCode == HttpStatusCode.Conflict));

            var studyInstanceUID = dicomFile.Dataset.GetSingleValue<string>(DicomTag.StudyInstanceUID);
            var seriesInstanceUID = dicomFile.Dataset.GetSingleValue<string>(DicomTag.SeriesInstanceUID);
            var sopInstanceUID = dicomFile.Dataset.GetSingleValue<string>(DicomTag.SOPInstanceUID);

            var getResponse = await Client.GetInstanceAsync(studyInstanceUID, seriesInstanceUID, sopInstanceUID, transferSyntax);

            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
            Assert.Null(getResponse.Value.Single());
        }

        [Theory]
        [InlineData("application/data")]
        [InlineData("application/json")]
        public async Task GivenAnIncorrectAcceptHeader_WhenRetrievingStudy_NotAcceptableIsReturned(string acceptHeader)
        {
            await ValidateNotAcceptableResponseAsync(
                string.Format(DicomWebClient.BaseRetrieveStudyUriFormat, Guid.NewGuid().ToString()),
                acceptHeader);
        }

        [Theory]
        [InlineData("application/data")]
        [InlineData("application/json")]
        public async Task GivenAnIncorrectAcceptHeader_WhenRetrievingSeries_NotAcceptableIsReturned(string acceptHeader)
        {
            await ValidateNotAcceptableResponseAsync(
                string.Format(DicomWebClient.BaseRetrieveSeriesUriFormat, Guid.NewGuid().ToString(), Guid.NewGuid().ToString()),
                acceptHeader);
        }

        [Theory]
        [InlineData("application/data")]
        [InlineData("application/json")]
        public async Task GivenAnIncorrectAcceptHeader_WhenRetrievingInstance_NotAcceptableIsReturned(string acceptHeader)
        {
            await ValidateNotAcceptableResponseAsync(
                string.Format(DicomWebClient.BaseRetrieveInstanceUriFormat, Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), Guid.NewGuid().ToString()),
                acceptHeader);
        }

        [Theory]
        [InlineData("application/dicom")]
        [InlineData("application/data")]
        [InlineData("application/json")]
        public async Task GivenAnIncorrectAcceptHeader_WhenRetrievingFrames_NotAcceptableIsReturned(string acceptHeader)
        {
            await ValidateNotAcceptableResponseAsync(
                string.Format(DicomWebClient.BaseRetrieveFramesUriFormat, Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), 1),
                acceptHeader);
        }

        private async Task ValidateNotAcceptableResponseAsync(string requestUri, string acceptHeader)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Add(HeaderNames.Accept, acceptHeader);
            using (HttpResponseMessage response = await Client.HttpClient.SendAsync(request))
            {
                Assert.Equal(HttpStatusCode.NotAcceptable, response.StatusCode);
            }
        }

        private void ValidateRetrieveTransaction(
            HttpResult<IReadOnlyList<DicomFile>> response,
            HttpStatusCode expectedStatusCode,
            DicomTransferSyntax expectedTransferSyntax,
            params DicomFile[] expectedFiles)
        {
            Assert.Equal(expectedStatusCode, response.StatusCode);
            Assert.Equal(expectedFiles.Length, response.Value.Count);

            for (var i = 0; i < expectedFiles.Length; i++)
            {
                DicomFile expectedFile = expectedFiles[i];
                var expectedInstance = DicomInstance.Create(expectedFile.Dataset);
                DicomFile actualFile = response.Value.First(x => DicomInstance.Create(x.Dataset).Equals(expectedInstance));

                Assert.Equal(expectedTransferSyntax, response.Value[i].Dataset.InternalTransferSyntax);

                // If the same transfer syntax as original, the files should be exactly the same
                if (expectedFile.Dataset.InternalTransferSyntax == actualFile.Dataset.InternalTransferSyntax)
                {
                    var expectedFileArray = DicomFileToByteArray(expectedFile);
                    var actualFileArray = DicomFileToByteArray(actualFile);

                    Assert.Equal(expectedFileArray.Length, actualFileArray.Length);

                    for (var ii = 0; ii < expectedFileArray.Length; ii++)
                    {
                        Assert.Equal(expectedFileArray[ii], actualFileArray[ii]);
                    }
                }
                else
                {
                    throw new NotImplementedException();
                }
            }
        }

        private static byte[] DicomFileToByteArray(DicomFile dicomFile)
        {
            using (var memoryStream = new MemoryStream())
            {
                dicomFile.Save(memoryStream);
                return memoryStream.ToArray();
            }
        }
    }
}
