using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Graphics;

namespace SpectraEngine.Core.Assets;

public sealed partial class AssetManager
{
    private AssetUploadPipeline? _uploadPipeline;
    private AssetUploadBudget _uploadBudget = new();
    /// <summary>Configure before attaching a renderer. Shared by models and textures.</summary>
    public AssetUploadBudget UploadBudget
    {
        get => _uploadBudget;
        set
        {
            ArgumentNullException.ThrowIfNull(value); value.Validate();
            if (_renderer is not null) throw new InvalidOperationException("Configure upload budgets before attaching a renderer.");
            _uploadBudget = value;
        }
    }
    public AssetQueueStatistics QueueStatistics => _uploadPipeline?.Snapshot ?? default;

    private bool TextureRequestStale(TextureAsset asset, long sequence) =>
        sequence != asset.RequestSequence || sequence <= asset.AppliedSequence || !IsCachedVariant(asset);
    private bool ModelRequestStale(ModelAsset asset, long sequence) =>
        sequence != asset.RequestSequence || sequence <= asset.AppliedSequence || !IsCachedModel(asset);

    private ImageSource PrepareImage(TextureAsset asset, ImageSource image)
    {
        try
        {
            var desc = ImageDescriptor(image, asset.ColorSpace, asset.Filter, asset.Wrap);
            if (_renderer!.PrepareTextureUpload(desc) is not { } prepared) return image;
            image.Dispose();
            return new(null, null, null, prepared);
        }
        catch { image.Dispose(); throw; }
    }

    private static TextureUploadDesc ImageDescriptor(ImageSource image, TextureColorSpace space, TextureFilter filter, TextureWrap wrap)
    {
        if (image.Prepared is { } prepared) return new(prepared.Format, space, prepared.Payload, prepared.Mips, filter, wrap);
        if (image.Decoded is { } decoded) return TextureUploadDesc.SingleLevel(decoded.Pixels, decoded.Width, decoded.Height, decoded.Format, space, filter, wrap);
        return new(image.Cooked!.Format, space, image.Blob!.Span, image.Cooked.Mips, filter, wrap);
    }

    private sealed class TextureJob : IAssetUpload
    {
        private readonly AssetManager _owner;
        private readonly UploadRequest _request;
        private TextureUpload? _upload;
        private bool _disposed;
        public long PayloadBytes { get; }
        public bool IsStale => _owner.TextureRequestStale(_request.Asset, _request.Sequence);
        internal TextureJob(AssetManager owner, UploadRequest request)
        { _owner = owner; _request = request; PayloadBytes = request.Image.Payload.Length; }
        public AssetUploadStep Step(int maxBytes)
        {
            var asset = _request.Asset;
            if (!_request.Image.HasContent) return new(0, true, _owner.ApplyUpload(_request));
            try
            {
                int bytes = 0;
                if (_upload is null)
                {
                    _owner.WarnIfSrgbUnavailable(asset.RelativePath, _request.Image.Format, asset.ColorSpace);
                    _upload = _owner._renderer!.BeginTextureUpload(ImageDescriptor(_request.Image, asset.ColorSpace, asset.Filter, asset.Wrap));
                    if (_upload.IsComplete) bytes = checked((int)PayloadBytes); // Indivisible fallback backend.
                }
                if (!_upload.IsComplete) bytes += _upload.Step(_request.Image.Payload, maxBytes);
                if (!_upload.IsComplete) return new(bytes, false, false);
                Texture texture = _upload.Complete();
                bool applied = _owner.ApplyUpload(_request, texture);
                if (!applied) _owner._renderer!.DestroyTexture(texture);
                return new(bytes, true, applied);
            }
            catch (Exception ex)
            {
                _owner.ApplyUpload(new UploadRequest(asset, _request.Sequence, default, ex.Message));
                return new(0, true, false);
            }
        }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            _upload?.Dispose(); _request.Image.Dispose(); _owner.EndDecode(_request.Asset);
        }
    }

    private sealed class ModelJob : IAssetUpload
    {
        private readonly AssetManager _owner;
        private readonly ModelAsset _asset;
        private readonly long _sequence;
        private readonly ModelData? _data;
        private readonly string? _error;
        private readonly bool _async;
        private readonly PreparedModelMaterial[] _prepared;
        private readonly ModelGeometry[] _geometry;
        private readonly Dictionary<ModelGeometry, SharedMeshStorage> _storage = new();
        private readonly Dictionary<ModelGeometry, ModelGeometry.PickingData> _picking = new();
        private readonly Mesh[] _meshes;
        private readonly Material[] _materials;
        private int _geometryIndex, _meshIndex, _materialIndex;
        private MeshUpload? _upload;
        private bool _published, _disposed;
        public long PayloadBytes { get; }
        public bool IsStale => _owner.ModelRequestStale(_asset, _sequence);

        internal ModelJob(AssetManager owner, ModelAsset asset, long sequence, ModelData? data, string? error, bool async)
        {
            _owner = owner; _asset = asset; _sequence = sequence; _data = data; _error = error; _async = async;
            var unique = new HashSet<ModelGeometry>();
            if (data is not null) foreach (var mesh in data.Meshes) unique.Add(mesh.Geometry);
            _geometry = new ModelGeometry[unique.Count]; unique.CopyTo(_geometry);
            foreach (var geometry in _geometry)
            {
                PayloadBytes += geometry.ByteLength;
                if (asset.Options.CpuRetention == ModelCpuRetention.Picking)
                {
                    var picking = geometry.PreparePickingData();
                    _picking.Add(geometry, picking);
                    PayloadBytes += picking.AdditionalBytes;
                }
            }
            _meshes = new Mesh[data?.Meshes.Count ?? 0]; _materials = new Material[data?.Materials.Count ?? 0];
            _prepared = data is null ? [] : owner.PrepareModelMaterials(data, async);
        }

        public AssetUploadStep Step(int maxBytes)
        {
            if (_data is null) { Fail(_error ?? "Model import failed."); return new(0, true, false); }
            try
            {
                if (_geometryIndex < _geometry.Length)
                {
                    var geometry = _geometry[_geometryIndex];
                    int bytes = 0;
                    if (_upload is null)
                    {
                        // The shared backing itself is never culled or drawn directly;
                        // each view supplies its authored bounds. Preparation owns all
                        // vertex scans, including picking data, off the render thread.
                        _upload = _owner._renderer!.BeginMeshUpload(geometry.Vertices, geometry.Indices, VertexAttribute.StandardLayout,
                            MeshCpuAccess.None, knownBounds: _data.LocalBounds);
                        if (_upload.IsComplete) bytes = checked((int)geometry.ByteLength);
                    }
                    if (!_upload.IsComplete) bytes += _upload.Step(maxBytes);
                    if (_upload.IsComplete)
                    {
                        var mesh = _upload.Complete();
                        if (_asset.Options.CpuRetention == ModelCpuRetention.Full) geometry.SharePickingData(mesh);
                        else if (_picking.TryGetValue(geometry, out var picking)) picking.Apply(mesh);
                        _storage.Add(geometry, new SharedMeshStorage(_owner._renderer!, mesh));
                        _upload.Dispose(); _upload = null; _geometryIndex++;
                    }
                    return new(bytes, false, false);
                }
                if (_meshIndex < _meshes.Length)
                {
                    var source = _data.Meshes[_meshIndex];
                    _meshes[_meshIndex++] = _storage[source.Geometry].CreateRange(source.DrawRange, source.LocalBounds);
                    return new(0, false, false);
                }
                if (_materialIndex < _materials.Length)
                {
                    _materials[_materialIndex] = _owner.ResolvePreparedModelMaterial(_prepared[_materialIndex], _async);
                    _materialIndex++;
                    return new(0, false, false);
                }
                _owner.PublishModel(_asset, _sequence, _data, _meshes, _materials);
                _published = true;
                return new(0, true, true);
            }
            catch (Exception ex) { Fail(ex.Message); return new(0, true, false); }
        }
        private void Fail(string error)
        {
            _asset.AppliedSequence = _sequence; _asset.Error = error;
            _owner._logger.LogError("Model import/upload failed ({Path}): {Error}", _asset.RelativePath, error);
        }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            _upload?.Dispose();
            if (!_published) for (int i = 0; i < _meshIndex; i++) _owner._renderer!.DestroyMesh(_meshes[i]);
            foreach (var storage in _storage.Values) storage.Dispose();
            _storage.Clear();
            if (_async) _owner.EndImport(_asset);
        }
    }
}
