using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    // Only metadata is resident for the whole year. Each season file is a sequence
    // of independently compressed variants; the mask file has a lazy-read index.
    public class CachedData
    {
        public class TextureData
        {
            public string name;
            public TextureProperties properties;
        }

        private sealed class Metadata
        {
            public uint revision;
            public Dictionary<string, PrefabController> controllers;
            public Dictionary<int, TextureData> textures;
        }

        internal sealed class VariantBlock
        {
            internal int TextureId;
            internal int Variant;
            internal byte[] Data;
            internal bool Raw;
        }

        private struct MaskBlock
        {
            internal long Offset;
            internal int Length;
        }

        private const uint MetadataMagic = 0x4D435353;
        private const uint SeasonMagic = 0x54435353;
        private const uint RecolorMagic = 0x52435353;
        private const int FormatVersion = 1;
        private const string MetadataFile = "cache.dat";
        private const string RecolorFile = "recolor.dat";
        private const string ExportFile = "cache.json";
        private const string TexturesDirectory = "textures";
        private const string PropertiesFile = "properties.json";
        private const string OriginalPostfix = ".orig.png";

        public readonly uint revision;
        public readonly Dictionary<string, PrefabController> controllers;
        public readonly Dictionary<int, TextureData> textures;

        private readonly bool m_json;
        private string m_directory;
        private readonly Dictionary<int, MaskBlock> m_masks = new Dictionary<int, MaskBlock>();

        private CachedData(uint revision, bool json, string directory,
            Dictionary<string, PrefabController> controllers, Dictionary<int, TextureData> textures)
        {
            this.revision = revision;
            m_json = json;
            m_directory = directory;
            this.controllers = controllers;
            this.textures = textures;
        }

        internal static string CacheDirectory(uint revision) => Path.Combine(cacheDirectory, revision.ToString());
        private static string SeasonPath(string directory, Season season) => Path.Combine(directory, season + ".dat");
        private static string TexturePath(string directory, int id) => Path.Combine(directory, TexturesDirectory, id.ToString());
        public static string SeasonFileName(Season season, int variant) => $"{season}_{variant + 1}.png";

        private static void WriteHeader(BinaryWriter writer, uint magic, uint revision, int kind)
        {
            writer.Write(magic);
            writer.Write(FormatVersion);
            writer.Write(revision);
            writer.Write(kind);
        }

        private static void ReadHeader(BinaryReader reader, uint magic, uint revision, int kind)
        {
            if (reader.ReadUInt32() != magic || reader.ReadInt32() != FormatVersion ||
                reader.ReadUInt32() != revision || reader.ReadInt32() != kind)
                throw new InvalidDataException("Invalid Seasons cache header.");
        }

        private static void WriteBlock(BinaryWriter writer, byte[] data)
        {
            byte[] compressed = BrotliCompressor.CompressBytes(data);
            writer.Write(data.Length);
            writer.Write(compressed.Length);
            writer.Write(compressed);
        }

        private static (int RawLength, int Length) ReadBlockHeader(BinaryReader reader, int expectedLength)
        {
            int rawLength = reader.ReadInt32();
            int length = reader.ReadInt32();
            if (rawLength <= 0 || (expectedLength > 0 && rawLength != expectedLength) ||
                length <= 0 || length > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new InvalidDataException("Invalid Seasons cache block size.");
            return (rawLength, length);
        }

        private static byte[] ReadBlock(BinaryReader reader, int expectedLength = 0)
        {
            var size = ReadBlockHeader(reader, expectedLength);
            byte[] compressed = reader.ReadBytes(size.Length);
            if (compressed.Length != size.Length)
                throw new EndOfStreamException();
            byte[] data = BrotliCompressor.DecompressBytes(compressed);
            if (data.Length != size.RawLength)
                throw new InvalidDataException("Invalid Seasons decompressed block size.");
            return data;
        }

        private static BinaryReader OpenReader(string path) =>
            new BinaryReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));

        private static void RequireEnd(BinaryReader reader)
        {
            if (reader.BaseStream.Position != reader.BaseStream.Length)
                throw new InvalidDataException("Unexpected trailing Seasons cache data.");
        }

        internal static CachedData Open(uint revision, CacheFormat format, string directory = null)
        {
            directory ??= CacheDirectory(revision);
            bool json = format == CacheFormat.Json;
            Metadata metadata;
            if (json)
            {
                metadata = new Metadata
                {
                    revision = revision,
                    controllers = JsonConvert.DeserializeObject<Dictionary<string, PrefabController>>(
                        File.ReadAllText(Path.Combine(directory, ExportFile))),
                    textures = new Dictionary<int, TextureData>()
                };

                foreach (string path in Directory.GetDirectories(Path.Combine(directory, TexturesDirectory)))
                {
                    if (!int.TryParse(Path.GetFileName(path), out int id))
                        continue;
                    string[] originals = Directory.GetFiles(path, "*" + OriginalPostfix);
                    if (originals.Length != 1)
                        throw new InvalidDataException($"Missing or ambiguous original texture name in {path}.");
                    string original = Path.GetFileName(originals[0]);
                    metadata.textures.Add(id, new TextureData
                    {
                        name = original.Substring(0, original.Length - OriginalPostfix.Length),
                        properties = JsonConvert.DeserializeObject<TextureProperties>(
                            File.ReadAllText(Path.Combine(path, PropertiesFile)))
                    });
                    foreach (Season season in Enum.GetValues(typeof(Season)))
                        for (int variant = 0; variant < seasonColorVariants; variant++)
                            if (!File.Exists(Path.Combine(path, SeasonFileName(season, variant))))
                                throw new InvalidDataException($"Missing {season} variant {variant} of texture {id}.");
                }
            }
            else
            {
                using BinaryReader reader = OpenReader(Path.Combine(directory, MetadataFile));
                ReadHeader(reader, MetadataMagic, revision, 0);
                metadata = JsonConvert.DeserializeObject<Metadata>(Encoding.UTF8.GetString(ReadBlock(reader)));
                RequireEnd(reader);
            }

            if (metadata == null || metadata.revision != revision ||
                metadata.controllers == null || metadata.controllers.Count == 0 ||
                metadata.textures == null || metadata.textures.Count == 0)
                throw new InvalidDataException("Incomplete Seasons cache metadata.");

            foreach (var entry in metadata.textures)
                if (entry.Value?.properties == null || entry.Value.name == null)
                    throw new InvalidDataException($"Missing metadata for texture {entry.Key}.");
                else
                    entry.Value.properties.RawDataLength();

            var cache = new CachedData(revision, json, directory, metadata.controllers, metadata.textures);
            if (!json)
                foreach (Season season in Enum.GetValues(typeof(Season)))
                {
                    using BinaryReader reader = OpenReader(SeasonPath(directory, season));
                    ReadHeader(reader, SeasonMagic, revision, (int)season);
                    if (reader.BaseStream.Position == reader.BaseStream.Length)
                        throw new InvalidDataException($"Empty {season} texture cache.");
                }

            // Index the mask records without decompressing any of their payloads.
            using (BinaryReader reader = OpenReader(Path.Combine(directory, RecolorFile)))
            {
                ReadHeader(reader, RecolorMagic, revision, 0);
                for (int i = 0; i < metadata.textures.Count; i++)
                {
                    int id = reader.ReadInt32();
                    if (!metadata.textures.TryGetValue(id, out TextureData texture))
                        throw new InvalidDataException($"Unknown recolor texture {id}.");
                    long offset = reader.BaseStream.Position;
                    var size = ReadBlockHeader(reader, checked((int)TextureVariants.RecolorDataLength(texture.properties)));
                    cache.m_masks.Add(id, new MaskBlock { Offset = offset, Length = size.RawLength });
                    reader.BaseStream.Seek(size.Length, SeekOrigin.Current);
                }
                RequireEnd(reader);
            }

            return cache;
        }

        internal IEnumerable<VariantBlock> ReadSeason(Season season)
        {
            if (m_json)
            {
                foreach (var entry in textures)
                    for (int variant = 0; variant < seasonColorVariants; variant++)
                        yield return new VariantBlock
                        {
                            TextureId = entry.Key,
                            Variant = variant,
                            Data = File.ReadAllBytes(Path.Combine(TexturePath(m_directory, entry.Key),
                                SeasonFileName(season, variant))),
                            Raw = false
                        };
                yield break;
            }

            using BinaryReader reader = OpenReader(SeasonPath(m_directory, season));
            ReadHeader(reader, SeasonMagic, revision, (int)season);
            var seen = new HashSet<(int, int)>();
            int count = checked(textures.Count * seasonColorVariants);
            for (int i = 0; i < count; i++)
            {
                int id = reader.ReadInt32();
                int variant = reader.ReadInt32();
                if (!textures.TryGetValue(id, out TextureData texture) ||
                    variant < 0 || variant >= seasonColorVariants || !seen.Add((id, variant)))
                    throw new InvalidDataException("Invalid or duplicate seasonal texture record.");

                yield return new VariantBlock
                {
                    TextureId = id,
                    Variant = variant,
                    Data = ReadBlock(reader, texture.properties.RawDataLength()),
                    Raw = true
                };
            }
            RequireEnd(reader);
        }

        private void ValidateSeasonFile(Season season)
        {
            using BinaryReader reader = OpenReader(SeasonPath(m_directory, season));
            ReadHeader(reader, SeasonMagic, revision, (int)season);
            var seen = new HashSet<(int, int)>();
            int count = checked(textures.Count * seasonColorVariants);
            for (int i = 0; i < count; i++)
            {
                int id = reader.ReadInt32();
                int variant = reader.ReadInt32();
                if (!textures.TryGetValue(id, out TextureData texture) ||
                    variant < 0 || variant >= seasonColorVariants || !seen.Add((id, variant)))
                    throw new InvalidDataException("Invalid or duplicate seasonal texture record.");
                var block = ReadBlockHeader(reader, texture.properties.RawDataLength());
                reader.BaseStream.Seek(block.Length, SeekOrigin.Current);
            }
            RequireEnd(reader);
        }

        internal bool HasRecolor(int id) => m_masks.ContainsKey(id);

        internal byte[] ReadRecolor(int id)
        {
            MaskBlock block = m_masks[id];
            using BinaryReader reader = OpenReader(Path.Combine(m_directory, RecolorFile));
            ReadHeader(reader, RecolorMagic, revision, 0);
            reader.BaseStream.Seek(block.Offset, SeekOrigin.Begin);
            return ReadBlock(reader, block.Length);
        }

        // Generation writes directly to the final file layout in a private staging
        // directory. Only one background write owns a pixel payload at a time.
        internal sealed class Writer : IDisposable
        {
            private readonly uint m_revision;
            private readonly CacheFormat m_format;
            private readonly bool m_binary;
            internal bool WritesJson { get; }
            private readonly string m_temp;
            private readonly BinaryWriter[] m_seasons = new BinaryWriter[seasonsCount];
            private BinaryWriter m_recolor;
            private Task m_pending;
            private byte[] m_rawBuffer;
            private Exception m_writeError;
            private bool m_published;
            private bool m_disposed;

            internal Writer(uint revision, CacheFormat format)
            {
                m_revision = revision;
                m_format = format;
                m_binary = format != CacheFormat.Json;
                WritesJson = format != CacheFormat.Binary;
                m_temp = Path.Combine(cacheDirectory, "temp", revision + "-" + Guid.NewGuid().ToString("N"));
                try
                {
                    Directory.CreateDirectory(m_temp);
                    if (m_binary)
                        foreach (Season season in Enum.GetValues(typeof(Season)))
                        {
                            var writer = new BinaryWriter(File.Create(SeasonPath(m_temp, season)));
                            m_seasons[(int)season] = writer;
                            WriteHeader(writer, SeasonMagic, revision, (int)season);
                        }

                    m_recolor = new BinaryWriter(File.Create(Path.Combine(m_temp, RecolorFile)));
                    WriteHeader(m_recolor, RecolorMagic, revision, 0);
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            private void WaitForWrite()
            {
                Task pending = m_pending;
                m_pending = null;
                try { pending?.GetAwaiter().GetResult(); }
                catch (Exception error) { m_writeError = error; }
                if (m_writeError != null)
                    throw new IOException("Unable to write the seasonal cache.", m_writeError);
            }

            internal void WriteOriginal(int id, string name, TextureProperties properties, Texture2D source)
            {
                if (!WritesJson)
                    return;
                WaitForWrite();
                string path = TexturePath(m_temp, id);
                byte[] png = source.EncodeToPNG();
                string settings = JsonConvert.SerializeObject(properties, Formatting.Indented);
                m_pending = Task.Run(() =>
                {
                    Directory.CreateDirectory(path);
                    File.WriteAllText(Path.Combine(path, PropertiesFile), settings);
                    File.WriteAllBytes(Path.Combine(path, name + OriginalPostfix), png);
                });
            }

            internal void WriteVariant(int id, Season season, int variant, Texture2D texture, TextureProperties properties)
            {
                // Do not extract the next payload until the previous one has been
                // written. The worker never touches a Unity object.
                WaitForWrite();
                byte[] raw = null;
                if (m_binary)
                {
                    var view = texture.GetRawTextureData<byte>();
                    if (view.Length != properties.RawDataLength())
                        throw new InvalidDataException($"Unexpected raw texture size for {id}.");
                    if (m_rawBuffer == null || m_rawBuffer.Length != view.Length)
                        m_rawBuffer = new byte[view.Length];
                    view.CopyTo(m_rawBuffer);
                    raw = m_rawBuffer;
                }
                byte[] png = WritesJson ? texture.EncodeToPNG() : null;
                m_pending = Task.Run(() =>
                {
                    if (raw != null)
                    {
                        BinaryWriter writer = m_seasons[(int)season];
                        writer.Write(id);
                        writer.Write(variant);
                        WriteBlock(writer, raw);
                    }
                    if (png != null)
                        File.WriteAllBytes(Path.Combine(TexturePath(m_temp, id), SeasonFileName(season, variant)), png);
                });
            }

            internal void WriteRecolor(int id, byte[] data)
            {
                WaitForWrite();
                m_pending = Task.Run(() =>
                {
                    m_recolor.Write(id);
                    WriteBlock(m_recolor, data);
                });
            }

            private void CloseStreams()
            {
                for (int i = 0; i < m_seasons.Length; i++)
                {
                    m_seasons[i]?.Dispose();
                    m_seasons[i] = null;
                }
                m_recolor?.Dispose();
                m_recolor = null;
            }

            internal CachedData Finish(Dictionary<string, PrefabController> controllers,
                Dictionary<int, TextureData> textures)
            {
                WaitForWrite();
                m_rawBuffer = null;
                CloseStreams();
                if (controllers.Count == 0 || textures.Count == 0)
                    throw new InvalidDataException("No seasonal texture data generated.");

                if (m_binary)
                {
                    var metadata = new Metadata { revision = m_revision, controllers = controllers, textures = textures };
                    byte[] data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(metadata));
                    using var writer = new BinaryWriter(File.Create(Path.Combine(m_temp, MetadataFile)));
                    WriteHeader(writer, MetadataMagic, m_revision, 0);
                    WriteBlock(writer, data);
                }
                if (WritesJson)
                    File.WriteAllText(Path.Combine(m_temp, ExportFile),
                        JsonConvert.SerializeObject(controllers, Formatting.Indented, new JsonSerializerSettings
                        {
                            NullValueHandling = NullValueHandling.Ignore,
                            DefaultValueHandling = DefaultValueHandling.Ignore
                        }));

                // Validate every season's record layout before publication.
                // This does not decompress or retain the pixel payloads.
                CachedData cache = Open(m_revision, m_format, m_temp);
                if (m_binary)
                    foreach (Season season in Enum.GetValues(typeof(Season)))
                        cache.ValidateSeasonFile(season);
                return cache;
            }

            internal void Publish(CachedData cache)
            {
                string destination = CacheDirectory(m_revision);
                string backup = m_temp + "-previous";
                bool replaced = Directory.Exists(destination);
                if (replaced)
                    Directory.Move(destination, backup);
                try
                {
                    Directory.Move(m_temp, destination);
                }
                catch
                {
                    if (replaced)
                        Directory.Move(backup, destination);
                    throw;
                }

                cache.m_directory = destination;
                m_published = true;
                if (replaced)
                    try { Directory.Delete(backup, true); }
                    catch (Exception error) { LogWarning($"Unable to remove previous Seasons cache: {error.Message}"); }
            }

            public void Dispose()
            {
                if (m_disposed)
                    return;
                m_disposed = true;
                try { WaitForWrite(); }
                catch (Exception error) { LogWarning($"Seasons cache write failed: {error.Message}"); }
                m_rawBuffer = null;
                try { CloseStreams(); }
                finally
                {
                    if (!m_published && Directory.Exists(m_temp))
                        try { Directory.Delete(m_temp, true); }
                        catch (Exception error) { LogWarning($"Unable to remove incomplete Seasons cache: {error.Message}"); }
                }
            }
        }
    }
}
