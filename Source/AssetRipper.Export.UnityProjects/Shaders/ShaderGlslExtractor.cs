using AssetRipper.SourceGenerated.Classes.ClassID_48;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader;
using K4os.Compression.LZ4;
using System.Text;

namespace AssetRipper.Export.UnityProjects.Shaders;

internal static class ShaderGlslExtractor
{
	private static readonly byte[][] TextMarkers =
	[
		Encoding.ASCII.GetBytes("#ifdef VERTEX"),
		Encoding.ASCII.GetBytes("#ifdef FRAGMENT"),
		Encoding.ASCII.GetBytes("#version"),
	];

	public static bool TryExtract(IShader shader, [NotNullWhen(true)] out IReadOnlyList<ShaderGlslSnippet>? snippets)
	{
		List<ShaderGlslSnippet> result = new();
		if (!shader.Has_CompressedBlob())
		{
			snippets = null;
			return false;
		}

		HashSet<string> seenText = new(StringComparer.Ordinal);
		byte[] compressedBlob = shader.CompressedBlob;
		GPUPlatform[] platforms = shader.GetPlatforms()?.ToArray() ?? [];

		if (shader.Has_Offsets_AssetList_AssetList_UInt32()
			&& shader.Has_CompressedLengths_AssetList_AssetList_UInt32()
			&& shader.Has_DecompressedLengths_AssetList_AssetList_UInt32())
		{
			int platformCount = Math.Min(shader.Offsets_AssetList_AssetList_UInt32.Count, Math.Min(shader.CompressedLengths_AssetList_AssetList_UInt32.Count, shader.DecompressedLengths_AssetList_AssetList_UInt32.Count));
			for (int platformIndex = 0; platformIndex < platformCount; platformIndex++)
			{
				GPUPlatform platform = platformIndex < platforms.Length ? platforms[platformIndex] : GPUPlatform.Unknown;
				ExtractFromChunkList(
					compressedBlob,
					shader.Offsets_AssetList_AssetList_UInt32[platformIndex],
					shader.CompressedLengths_AssetList_AssetList_UInt32[platformIndex],
					shader.DecompressedLengths_AssetList_AssetList_UInt32[platformIndex],
					platform,
					platformIndex,
					result,
					seenText);
			}
		}

		if (shader.Has_Offsets_AssetList_UInt32()
			&& shader.Has_CompressedLengths_AssetList_UInt32()
			&& shader.Has_DecompressedLengths_AssetList_UInt32())
		{
			ExtractFromChunkList(
				compressedBlob,
				shader.Offsets_AssetList_UInt32,
				shader.CompressedLengths_AssetList_UInt32,
				shader.DecompressedLengths_AssetList_UInt32,
				GPUPlatform.Unknown,
				-1,
				result,
				seenText);
		}

		snippets = result;
		return result.Count > 0;
	}

	private static void ExtractFromChunkList(
		byte[] compressedBlob,
		IReadOnlyList<uint> offsets,
		IReadOnlyList<uint> compressedLengths,
		IReadOnlyList<uint> decompressedLengths,
		GPUPlatform platform,
		int platformIndex,
		List<ShaderGlslSnippet> snippets,
		HashSet<string> seenText)
	{
		int count = Math.Min(offsets.Count, Math.Min(compressedLengths.Count, decompressedLengths.Count));
		for (int chunkIndex = 0; chunkIndex < count; chunkIndex++)
		{
			if (!TryDecompress(compressedBlob, offsets[chunkIndex], compressedLengths[chunkIndex], decompressedLengths[chunkIndex], out byte[]? decompressed))
			{
				continue;
			}

			foreach (string text in ExtractTextSnippets(decompressed))
			{
				if (seenText.Add(text))
				{
					snippets.Add(new ShaderGlslSnippet(platform, platformIndex, chunkIndex, text));
				}
			}
		}
	}

	private static bool TryDecompress(byte[] compressedBlob, uint offset, uint compressedLength, uint decompressedLength, [NotNullWhen(true)] out byte[]? decompressed)
	{
		decompressed = null;
		if (compressedLength == 0 || decompressedLength == 0 || offset > int.MaxValue || compressedLength > int.MaxValue || decompressedLength > int.MaxValue)
		{
			return false;
		}

		int start = (int)offset;
		int length = (int)compressedLength;
		if (start < 0 || length < 0 || start > compressedBlob.Length || length > compressedBlob.Length - start)
		{
			return false;
		}

		byte[] buffer = new byte[(int)decompressedLength];
		int written;
		try
		{
			written = LZ4Codec.Decode(compressedBlob.AsSpan(start, length), buffer);
		}
		catch (Exception)
		{
			return false;
		}
		if (written != buffer.Length)
		{
			return false;
		}

		decompressed = buffer;
		return true;
	}

	private static IEnumerable<string> ExtractTextSnippets(byte[] data)
	{
		int position = 0;
		while (TryFindNextMarker(data, position, out int start))
		{
			int end = start;
			while (end < data.Length && IsShaderTextByte(data[end]))
			{
				end++;
			}

			string text = Encoding.ASCII.GetString(data, start, end - start).Trim();
			if (LooksLikeGlsl(text))
			{
				yield return NormalizeLineEndings(text);
			}

			position = Math.Max(end, start + 1);
		}
	}

	private static bool TryFindNextMarker(ReadOnlySpan<byte> data, int start, out int index)
	{
		index = -1;
		foreach (byte[] marker in TextMarkers)
		{
			int candidate = IndexOf(data, marker, start);
			if (candidate >= 0 && (index < 0 || candidate < index))
			{
				index = candidate;
			}
		}
		return index >= 0;
	}

	private static int IndexOf(ReadOnlySpan<byte> data, ReadOnlySpan<byte> value, int start)
	{
		if (start >= data.Length)
		{
			return -1;
		}
		int relativeIndex = data[start..].IndexOf(value);
		return relativeIndex < 0 ? -1 : start + relativeIndex;
	}

	private static bool IsShaderTextByte(byte value)
	{
		return value is (byte)'\t' or (byte)'\n' or (byte)'\r' || value is >= 0x20 and <= 0x7E;
	}

	private static bool LooksLikeGlsl(string text)
	{
		return text.Contains("void main", StringComparison.Ordinal)
			&& (text.Contains("#version", StringComparison.Ordinal)
				|| text.Contains("attribute ", StringComparison.Ordinal)
				|| text.Contains("varying ", StringComparison.Ordinal)
				|| text.Contains("gl_Position", StringComparison.Ordinal)
				|| text.Contains("gl_FragData", StringComparison.Ordinal)
				|| text.Contains("gl_FragColor", StringComparison.Ordinal));
	}

	private static string NormalizeLineEndings(string text)
	{
		return text.Replace("\r\n", "\n").Replace('\r', '\n');
	}
}

internal sealed record ShaderGlslSnippet(GPUPlatform Platform, int PlatformIndex, int ChunkIndex, string Text);
