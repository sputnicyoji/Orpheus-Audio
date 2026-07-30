using System;
using UnityEngine;

namespace Orpheus.Audio.Editor
{
    [CreateAssetMenu(
        fileName = "OrpheusAudioAuthoringProfile",
        menuName = "Orpheus/Audio Authoring Profile")]
    public sealed class OrpheusAudioAuthoringProfile : ScriptableObject
    {
        [SerializeField] private int _schemaVersion = 1;
        [SerializeField] private OrpheusAudioKeyManifest _keyManifest;
        [SerializeField] private OrpheusAudioModuleRecipe[] _moduleRecipes =
            Array.Empty<OrpheusAudioModuleRecipe>();
        [SerializeField] private OrpheusAudioCatalog _catalog;
        [SerializeField] private string _generatedRoot =
            "Assets/Audio/OrpheusGenerated";

        internal OrpheusAudioAuthoringProfileValue CaptureValue()
        {
            if (_moduleRecipes == null)
            {
                return new OrpheusAudioAuthoringProfileValue(
                    _schemaVersion,
                    _keyManifest,
                    null,
                    _catalog,
                    _generatedRoot);
            }

            var recipes = new OrpheusAudioModuleRecipeValue?[_moduleRecipes.Length];
            for (var index = 0; index < _moduleRecipes.Length; index++)
            {
                var recipe = _moduleRecipes[index];
                recipes[index] = recipe == null
                    ? (OrpheusAudioModuleRecipeValue?)null
                    : recipe.CaptureValue();
            }

            return new OrpheusAudioAuthoringProfileValue(
                _schemaVersion,
                _keyManifest,
                recipes,
                _catalog,
                _generatedRoot);
        }
    }

    internal readonly struct OrpheusAudioAuthoringProfileValue
    {
        private readonly OrpheusAudioModuleRecipeValue?[] _moduleRecipes;

        internal OrpheusAudioAuthoringProfileValue(
            int schemaVersion,
            OrpheusAudioKeyManifest keyManifest,
            OrpheusAudioModuleRecipeValue?[] moduleRecipes,
            OrpheusAudioCatalog catalog,
            string generatedRoot)
        {
            SchemaVersion = schemaVersion;
            KeyManifest = keyManifest;
            _moduleRecipes = moduleRecipes == null
                ? null
                : (OrpheusAudioModuleRecipeValue?[])moduleRecipes.Clone();
            Catalog = catalog;
            GeneratedRoot = generatedRoot ?? string.Empty;
        }

        internal int SchemaVersion { get; }

        internal OrpheusAudioKeyManifest KeyManifest { get; }

        internal bool HasModuleRecipeStorage => _moduleRecipes != null;

        internal int ModuleRecipeCount =>
            _moduleRecipes == null ? 0 : _moduleRecipes.Length;

        internal OrpheusAudioCatalog Catalog { get; }

        internal string GeneratedRoot { get; }

        internal bool TryGetModuleRecipe(
            int index,
            out OrpheusAudioModuleRecipeValue recipe)
        {
            var candidate = _moduleRecipes[index];
            recipe = candidate.GetValueOrDefault();
            return candidate.HasValue;
        }
    }
}
