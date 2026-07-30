using System;
using System.Collections.Generic;
using System.IO;
using Orpheus.Audio.Core;
using UnityEditor;
using UnityEngine;

namespace Orpheus.Audio.Editor
{
    internal static class OrpheusAudioAuthoringReadOnlyValidation
    {
        internal static List<OrpheusAudioValidationError> Validate(
            OrpheusAudioValidationProfile profile,
            BuildTargetGroup targetGroup,
            OrpheusAudioValidationProfile[] discoveredProfiles)
        {
            var errors = new List<OrpheusAudioValidationError>();
            if (profile == null)
            {
                return errors;
            }

            var profilePath = AssetDatabase.GetAssetPath(profile);
            var authoring = profile.AuthoringProfile;
            var enrollmentGuid =
                profile.AuthoringEnrollmentGuid ?? string.Empty;
            if (authoring == null && enrollmentGuid.Length == 0)
            {
                return errors;
            }

            if (authoring != null && enrollmentGuid.Length == 0)
            {
                Add(
                    errors,
                    OrpheusAudioValidationErrorCode.InvalidAuthoringProfile,
                    profilePath,
                    AssetDatabase.GetAssetPath(authoring));
                return errors;
            }

            var authoringPath = AssetDatabase.GetAssetPath(authoring);
            var authoringGuid =
                AssetDatabase.AssetPathToGUID(authoringPath)
                    .ToLowerInvariant();
            if (authoring == null ||
                string.IsNullOrEmpty(authoringGuid) ||
                !string.Equals(
                    enrollmentGuid,
                    authoringGuid,
                    StringComparison.Ordinal))
            {
                Add(
                    errors,
                    OrpheusAudioValidationErrorCode
                        .AuthoringEnrollmentIdentityMismatch,
                    profilePath,
                    authoringPath);
                return errors;
            }

            if (!OrpheusAudioAuthoringCapture.TryCaptureDetailed(
                    profile,
                    OrpheusAudioAuthoringCompileMode.Analyze,
                    targetGroup,
                    discoveredProfiles,
                    out var input,
                    out var plan,
                    out _,
                    out var captureErrors))
            {
                AddMappedCaptureErrors(
                    errors,
                    profilePath,
                    authoring.CaptureValue().GeneratedRoot,
                    captureErrors);
                return errors;
            }

            var catalogMismatch = HasCatalogMismatch(input, plan);
            if (catalogMismatch)
            {
                Add(
                    errors,
                    OrpheusAudioValidationErrorCode
                        .GeneratedCatalogMismatch,
                    profilePath,
                    plan.CatalogAssetPath);
            }

            var closurePath = string.Concat(
                authoring.CaptureValue().GeneratedRoot,
                "/",
                "OrpheusAuthoringClosure.json");
            var closureAbsolute = ProjectAbsolute(closurePath);
            if (!File.Exists(closureAbsolute) ||
                !OrpheusAudioAuthoringReports.TryReadClosureFingerprints(
                    File.ReadAllText(closureAbsolute),
                    out var inputFingerprint,
                    out var outputFingerprint))
            {
                Add(
                    errors,
                    OrpheusAudioValidationErrorCode
                        .StaleGeneratedOwnership,
                    profilePath,
                    closurePath);
                return errors;
            }

            if (!string.Equals(
                    inputFingerprint,
                    plan.InputFingerprint,
                    StringComparison.Ordinal))
            {
                Add(
                    errors,
                    OrpheusAudioValidationErrorCode.StaleAuthoringInput,
                    profilePath,
                    closurePath);
            }

            if (!string.Equals(
                    outputFingerprint,
                    plan.OutputFingerprint,
                    StringComparison.Ordinal))
            {
                Add(
                    errors,
                    OrpheusAudioValidationErrorCode
                        .AuthoringOutputFingerprintMismatch,
                    profilePath,
                    closurePath);
            }

            if (!catalogMismatch &&
                OrpheusAudioAuthoringCompiler.GetSuccessStatus(input, plan) ==
                OrpheusAudioAuthoringCompileStatus.SucceededChanged &&
                !Contains(
                    errors,
                    OrpheusAudioValidationErrorCode.StaleAuthoringInput) &&
                !Contains(
                    errors,
                    OrpheusAudioValidationErrorCode
                        .AuthoringOutputFingerprintMismatch))
            {
                Add(
                    errors,
                    OrpheusAudioValidationErrorCode.StaleAuthoringInput,
                    profilePath,
                    closurePath);
            }

            return errors;
        }

        private static bool HasCatalogMismatch(
            OrpheusAuthoringCompilationInput input,
            OrpheusAuthoringCompilationPlan plan)
        {
            if (input.Catalog == null ||
                input.Catalog.EventCount != plan.EventCount ||
                !input.Catalog.HasEventAssetPaths)
            {
                return true;
            }

            for (var index = 0; index < plan.EventCount; index++)
            {
                if (!string.Equals(
                        input.Catalog.GetEventAssetPath(index),
                        plan.GetEvent(index).AssetPath,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddMappedCaptureErrors(
            List<OrpheusAudioValidationError> errors,
            string profilePath,
            string generatedRoot,
            OrpheusAuthoringCompilationError[] captureErrors)
        {
            if (captureErrors == null)
            {
                Add(
                    errors,
                    OrpheusAudioValidationErrorCode.InvalidAuthoringProfile,
                    profilePath,
                    profilePath);
                return;
            }

            for (var index = 0; index < captureErrors.Length; index++)
            {
                var source = captureErrors[index];
                var code = Map(source.Code);
                if (Contains(errors, code))
                {
                    continue;
                }

                var path = string.IsNullOrEmpty(source.AssetPath)
                    ? generatedRoot
                    : source.AssetPath;
                Add(errors, code, profilePath, path);
            }
        }

        private static OrpheusAudioValidationErrorCode Map(
            OrpheusAuthoringErrorCode code)
        {
            switch (code)
            {
                case OrpheusAuthoringErrorCode.EnrollmentIdentityMismatch:
                    return OrpheusAudioValidationErrorCode
                        .AuthoringEnrollmentIdentityMismatch;
                case OrpheusAuthoringErrorCode.LostOwnershipState:
                    return OrpheusAudioValidationErrorCode
                        .LostAuthoringOwnership;
                case OrpheusAuthoringErrorCode.OwnershipStateInvalid:
                case OrpheusAuthoringErrorCode
                    .TransactionRecoveryRequired:
                case OrpheusAuthoringErrorCode.RollbackFailed:
                    return OrpheusAudioValidationErrorCode
                        .StaleGeneratedOwnership;
                case OrpheusAuthoringErrorCode.CatalogMismatch:
                case OrpheusAuthoringErrorCode
                    .CatalogNotEmptyAtEnrollment:
                    return OrpheusAudioValidationErrorCode
                        .GeneratedCatalogMismatch;
                case OrpheusAuthoringErrorCode
                    .ActualOutputFingerprintMismatch:
                    return OrpheusAudioValidationErrorCode
                        .AuthoringOutputFingerprintMismatch;
                case OrpheusAuthoringErrorCode.InvalidValidationProfile:
                case OrpheusAuthoringErrorCode
                    .DisabledValidationProfile:
                case OrpheusAuthoringErrorCode
                    .ManualProfileNotCompilable:
                case OrpheusAuthoringErrorCode.AuthoringProfileConflict:
                    return OrpheusAudioValidationErrorCode
                        .InvalidAuthoringProfile;
                default:
                    return OrpheusAudioValidationErrorCode
                        .StaleAuthoringInput;
            }
        }

        private static bool Contains(
            List<OrpheusAudioValidationError> errors,
            OrpheusAudioValidationErrorCode code)
        {
            for (var index = 0; index < errors.Count; index++)
            {
                if (errors[index].Code == code)
                {
                    return true;
                }
            }

            return false;
        }

        private static void Add(
            List<OrpheusAudioValidationError> errors,
            OrpheusAudioValidationErrorCode code,
            string profilePath,
            string assetPath)
        {
            errors.Add(
                new OrpheusAudioValidationError(
                    code,
                    profilePath,
                    assetPath));
        }

        private static string ProjectAbsolute(string path)
        {
            return Path.GetFullPath(
                Path.Combine(
                    Directory.GetParent(Application.dataPath).FullName,
                    path));
        }
    }
}
