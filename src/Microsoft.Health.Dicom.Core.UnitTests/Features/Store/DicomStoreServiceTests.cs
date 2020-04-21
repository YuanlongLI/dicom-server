﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Dicom;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Dicom.Core.Exceptions;
using Microsoft.Health.Dicom.Core.Features.Store;
using Microsoft.Health.Dicom.Core.Features.Store.Entries;
using Microsoft.Health.Dicom.Core.Messages.Store;
using Microsoft.Health.Dicom.Tests.Common;
using NSubstitute;
using Xunit;
using DicomValidationException = Dicom.DicomValidationException;

namespace Microsoft.Health.Dicom.Core.UnitTests.Features.Store
{
    public class DicomStoreServiceTests
    {
        private static readonly CancellationToken DefaultCancellationToken = new CancellationTokenSource().Token;
        private static readonly DicomStoreResponse DefaultResponse = new DicomStoreResponse(HttpStatusCode.OK);

        private readonly DicomDataset _dicomDataset1 = Samples.CreateRandomInstanceDataset(
            studyInstanceUid: "1",
            seriesInstanceUid: "2",
            sopInstanceUid: "3",
            sopClassUid: "4");

        private readonly DicomDataset _dicomDataset2 = Samples.CreateRandomInstanceDataset(
            studyInstanceUid: "10",
            seriesInstanceUid: "11",
            sopInstanceUid: "12",
            sopClassUid: "13");

        private readonly IDicomStoreResponseBuilder _dicomStoreResponseBuilder = Substitute.For<IDicomStoreResponseBuilder>();
        private readonly IDicomDatasetMinimumRequirementValidator _dicomDatasetMinimumRequirementValidator = Substitute.For<IDicomDatasetMinimumRequirementValidator>();
        private readonly IDicomStoreOrchestrator _dicomStoreOrchestrator = Substitute.For<IDicomStoreOrchestrator>();
        private readonly DicomStoreService _dicomStoreService;

        public DicomStoreServiceTests()
        {
            _dicomStoreResponseBuilder.BuildResponse(Arg.Any<string>()).Returns(DefaultResponse);

            _dicomStoreService = new DicomStoreService(
                _dicomStoreResponseBuilder,
                _dicomDatasetMinimumRequirementValidator,
                _dicomStoreOrchestrator,
                NullLogger<DicomStoreService>.Instance);
        }

        [Fact]
        public async Task GivenNullDicomInstanceEntries_WhenProcessed_ThenNoContentShouldBeReturned()
        {
            await ExecuteAndValidateAsync(dicomInstanceEntries: null);

            _dicomStoreResponseBuilder.DidNotReceiveWithAnyArgs().AddSuccess(default);
            _dicomStoreResponseBuilder.DidNotReceiveWithAnyArgs().AddFailure(default);
        }

        [Fact]
        public async Task GivenEmptyDicomInstanceEntries_WhenProcessed_ThenNoContentShouldBeReturned()
        {
            await ExecuteAndValidateAsync(new IDicomInstanceEntry[0]);

            _dicomStoreResponseBuilder.DidNotReceiveWithAnyArgs().AddSuccess(default);
            _dicomStoreResponseBuilder.DidNotReceiveWithAnyArgs().AddFailure(default);
        }

        [Fact]
        public async Task GivenAValidDicomInstanceEntry_WhenProcessed_ThenSuccessfulEntryShouldBeAdded()
        {
            IDicomInstanceEntry dicomInstanceEntry = Substitute.For<IDicomInstanceEntry>();

            dicomInstanceEntry.GetDicomDatasetAsync(DefaultCancellationToken).Returns(_dicomDataset1);

            await ExecuteAndValidateAsync(dicomInstanceEntry);

            _dicomStoreResponseBuilder.Received(1).AddSuccess(_dicomDataset1);
            _dicomStoreResponseBuilder.DidNotReceiveWithAnyArgs().AddFailure(default);
        }

        [Fact]
        public async Task GiveAnInvalidDicomDataset_WhenProcessed_ThenFailedEntryShouldBeAddedWithProcessingFailure()
        {
            IDicomInstanceEntry dicomInstanceEntry = Substitute.For<IDicomInstanceEntry>();

            dicomInstanceEntry.GetDicomDatasetAsync(DefaultCancellationToken).Returns<DicomDataset>(_ => throw new Exception());

            await ExecuteAndValidateAsync(dicomInstanceEntry);

            _dicomStoreResponseBuilder.DidNotReceiveWithAnyArgs().AddSuccess(default);
            _dicomStoreResponseBuilder.Received(1).AddFailure(null, TestConstants.ProcessingFailureReasonCode);
        }

        [Fact]
        public async Task GivenADicomDatasetFailsToOpenDueToDicomValidationException_WhenProcessed_ThenFailedEntryShouldBeAddedWithValidationFailure()
        {
            IDicomInstanceEntry dicomInstanceEntry = Substitute.For<IDicomInstanceEntry>();

            dicomInstanceEntry.GetDicomDatasetAsync(DefaultCancellationToken).Returns<DicomDataset>(_ => throw new DicomValidationException("value", DicomVR.UI, string.Empty));

            await ExecuteAndValidateAsync(dicomInstanceEntry);

            _dicomStoreResponseBuilder.DidNotReceiveWithAnyArgs().AddSuccess(default);
            _dicomStoreResponseBuilder.Received(1).AddFailure(null, TestConstants.ValidationFailureReasonCode);
        }

        [Fact]
        public async Task GivenAValidationError_WhenProcessed_ThenFailedEntryShouldBeAddedWithValidationFailure()
        {
            const ushort failureCode = 500;

            _dicomDatasetMinimumRequirementValidator
                .When(validator => validator.Validate(Arg.Any<DicomDataset>(), Arg.Any<string>()))
                .Do(_ => throw new DicomDatasetValidationException(failureCode, "test"));

            IDicomInstanceEntry dicomInstanceEntry = Substitute.For<IDicomInstanceEntry>();

            dicomInstanceEntry.GetDicomDatasetAsync(DefaultCancellationToken).Returns(_dicomDataset2);

            await ExecuteAndValidateAsync(dicomInstanceEntry);

            _dicomStoreResponseBuilder.DidNotReceiveWithAnyArgs().AddSuccess(default);
            _dicomStoreResponseBuilder.Received(1).AddFailure(_dicomDataset2, failureCode);
        }

        [Fact]
        public async Task GivenADicomInstanceAlreadyExistsExceptionWithConflictWhenStoring_WhenProcessed_ThenFailedEntryShouldBeAddedWithSopInstanceAlreadyExists()
        {
            IDicomInstanceEntry dicomInstanceEntry = Substitute.For<IDicomInstanceEntry>();

            dicomInstanceEntry.GetDicomDatasetAsync(DefaultCancellationToken).Returns(_dicomDataset2);

            _dicomStoreOrchestrator
                .When(dicomStoreService => dicomStoreService.StoreDicomInstanceEntryAsync(dicomInstanceEntry, DefaultCancellationToken))
                .Do(_ => throw new DicomInstanceAlreadyExistsException());

            await ExecuteAndValidateAsync(dicomInstanceEntry);

            _dicomStoreResponseBuilder.DidNotReceiveWithAnyArgs().AddSuccess(default);
            _dicomStoreResponseBuilder.Received(1).AddFailure(_dicomDataset2, TestConstants.SopInstanceAlreadyExistsReasonCode);
        }

        [Fact]
        public async Task GivenAnExceptionWhenStoring_WhenProcessed_ThenFailedEntryShouldBeAddedWithProcessingFailure()
        {
            IDicomInstanceEntry dicomInstanceEntry = Substitute.For<IDicomInstanceEntry>();

            dicomInstanceEntry.GetDicomDatasetAsync(DefaultCancellationToken).Returns(_dicomDataset2);

            _dicomStoreOrchestrator
                .When(dicomStoreService => dicomStoreService.StoreDicomInstanceEntryAsync(dicomInstanceEntry, DefaultCancellationToken))
                .Do(_ => throw new DicomDataStoreException());

            await ExecuteAndValidateAsync(dicomInstanceEntry);

            _dicomStoreResponseBuilder.DidNotReceiveWithAnyArgs().AddSuccess(default);
            _dicomStoreResponseBuilder.Received(1).AddFailure(_dicomDataset2, TestConstants.ProcessingFailureReasonCode);
        }

        [Fact]
        public async Task GivenMultipleDicomInstanceEntries_WhenProcessed_ThenCorrespondingEntryShouldBeAdded()
        {
            IDicomInstanceEntry dicomInstanceEntryToSucceed = Substitute.For<IDicomInstanceEntry>();
            IDicomInstanceEntry dicomInstanceEntryToFail = Substitute.For<IDicomInstanceEntry>();

            dicomInstanceEntryToSucceed.GetDicomDatasetAsync(DefaultCancellationToken).Returns(_dicomDataset1);
            dicomInstanceEntryToFail.GetDicomDatasetAsync(DefaultCancellationToken).Returns(_dicomDataset2);

            _dicomDatasetMinimumRequirementValidator
                .When(dicomDatasetMinimumRequirementValidator => dicomDatasetMinimumRequirementValidator.Validate(_dicomDataset2, null))
                .Do(_ => throw new Exception());

            await ExecuteAndValidateAsync(dicomInstanceEntryToSucceed, dicomInstanceEntryToFail);

            _dicomStoreResponseBuilder.Received(1).AddSuccess(_dicomDataset1);
            _dicomStoreResponseBuilder.Received(1).AddFailure(_dicomDataset2, TestConstants.ProcessingFailureReasonCode);
        }

        [Fact]
        public async Task GivenRequiredStudyInstanceUid_WhenProcessed_ThenItShouldBePassed()
        {
            IDicomInstanceEntry dicomInstanceEntry = Substitute.For<IDicomInstanceEntry>();

            dicomInstanceEntry.GetDicomDatasetAsync(DefaultCancellationToken).Returns(_dicomDataset2);

            await ExecuteAndValidateAsync(dicomInstanceEntry);
        }

        private Task ExecuteAndValidateAsync(params IDicomInstanceEntry[] dicomInstanceEntries)
            => ExecuteAndValidateAsync(requiredStudyInstanceUid: null, dicomInstanceEntries);

        private async Task ExecuteAndValidateAsync(
            string requiredStudyInstanceUid,
            params IDicomInstanceEntry[] dicomInstanceEntries)
        {
            DicomStoreResponse response = await _dicomStoreService.ProcessAsync(
                dicomInstanceEntries,
                requiredStudyInstanceUid,
                cancellationToken: DefaultCancellationToken);

            Assert.Equal(DefaultResponse, response);

            _dicomStoreResponseBuilder.Received(1).BuildResponse(requiredStudyInstanceUid);

            if (dicomInstanceEntries != null)
            {
                foreach (IDicomInstanceEntry dicomInstanceEntry in dicomInstanceEntries)
                {
                    await ValidateDisposeAsync(dicomInstanceEntry);
                }
            }
        }

        private async Task ValidateDisposeAsync(IDicomInstanceEntry dicomInstanceEntry)
        {
            var timeout = DateTime.Now.AddSeconds(5);

            while (timeout < DateTime.Now)
            {
                if (dicomInstanceEntry.ReceivedCalls().Any())
                {
                    await dicomInstanceEntry.Received(1).DisposeAsync();
                    break;
                }

                await Task.Delay(100);
            }
        }
    }
}