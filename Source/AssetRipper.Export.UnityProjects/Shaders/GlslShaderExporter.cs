using AssetRipper.Assets;
using AssetRipper.SourceGenerated.Classes.ClassID_48;
using AssetRipper.SourceGenerated.Extensions;
using System.Globalization;

namespace AssetRipper.Export.UnityProjects.Shaders;

public sealed class GlslShaderExporter : ShaderExporterBase
{
	public override bool Export(IExportContainer container, IUnityObjectBase asset, string path, FileSystem fileSystem)
	{
		IShader shader = (IShader)asset;
		if (!ShaderGlslExtractor.TryExtract(shader, out IReadOnlyList<ShaderGlslSnippet>? snippets))
		{
			return DummyShaderTextExporter.ExportShader(shader, path, fileSystem);
		}

		using Stream fileStream = fileSystem.File.Create(path);
		using InvariantStreamWriter writer = new(fileStream);
		Export(shader, snippets, writer);
		return true;
	}

	private static void Export(IShader shader, IReadOnlyList<ShaderGlslSnippet> snippets, TextWriter writer)
	{
		writer.Write($"Shader \"{EscapeShaderString(GetShaderName(shader))}\" {{\n");
		if (shader.Has_ParsedForm())
		{
			DummyShaderTextExporter.ExportProperties(shader.ParsedForm.PropInfo, writer);
		}

		writer.WriteIndent(1);
		writer.Write("SubShader {\n");
		writer.WriteIndent(2);
		writer.Write("Tags { \"RenderType\" = \"Opaque\" }\n");
		writer.WriteIndent(2);
		writer.Write("// Extracted GLSL subprograms. Variant and pass grouping is approximate.\n");

		for (int i = 0; i < snippets.Count; i++)
		{
			WriteSnippet(writer, snippets[i], i);
		}

		writer.WriteIndent(1);
		writer.Write("}\n");

		if (shader.Has_ParsedForm())
		{
			if (shader.ParsedForm.FallbackName != string.Empty)
			{
				writer.WriteIndent(1);
				writer.Write($"Fallback \"{EscapeShaderString(shader.ParsedForm.FallbackName)}\"\n");
			}
			if (shader.ParsedForm.CustomEditorName != string.Empty)
			{
				writer.WriteIndent(1);
				writer.Write($"//CustomEditor \"{EscapeShaderString(shader.ParsedForm.CustomEditorName)}\"\n");
			}
		}

		writer.Write('}');
	}

	private static void WriteSnippet(TextWriter writer, ShaderGlslSnippet snippet, int index)
	{
		writer.WriteIndent(2);
		writer.Write("Pass {\n");
		writer.WriteIndent(3);
		writer.Write($"Name \"GLSL_{index.ToString(CultureInfo.InvariantCulture)}\"\n");
		writer.WriteIndent(3);
		writer.Write($"// Platform: {snippet.Platform} (index {snippet.PlatformIndex.ToString(CultureInfo.InvariantCulture)}), compressed chunk: {snippet.ChunkIndex.ToString(CultureInfo.InvariantCulture)}\n");
		writer.WriteIndent(3);
		writer.Write("GLSLPROGRAM\n");
		WriteIndentedSnippet(writer, snippet.Text, 3);
		writer.WriteIndent(3);
		writer.Write("ENDGLSL\n");
		writer.WriteIndent(2);
		writer.Write("}\n");
	}

	private static void WriteIndentedSnippet(TextWriter writer, string text, int indent)
	{
		using StringReader reader = new(text);
		string? line;
		while ((line = reader.ReadLine()) is not null)
		{
			if (line.Length > 0)
			{
				writer.WriteIndent(indent);
			}
			writer.Write(line);
			writer.Write('\n');
		}
	}

	private static string GetShaderName(IShader shader)
	{
		return shader.Has_ParsedForm() && !string.IsNullOrEmpty(shader.ParsedForm.Name) ? shader.ParsedForm.Name : shader.GetBestName();
	}

	private static string EscapeShaderString(string value)
	{
		return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
	}
}
