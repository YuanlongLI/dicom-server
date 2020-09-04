﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.DicomCast.Core.Features.Fhir
{
    /// <summary>
    /// Exception thrown when a <see cref="Hl7.Fhir.Model.Resource"/> fails validation.
    /// </summary>
    public class FhirResourceValidationException : Exception
    {
        public FhirResourceValidationException(string message)
            : base(message)
        {
        }
    }
}