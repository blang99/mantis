using System.Collections.Generic;
using System.Text.Json;
using Mantis.Plugin.AI;
using Xunit;

namespace Mantis.Tests;

/// <summary>
/// Phase 1 image-to-script acceptance gate. <see cref="VisionPayload"/> maps a
/// provider-agnostic <see cref="ChatMessage"/> into each LLM's wire format. These
/// tests serialize the output through <c>System.Text.Json</c> exactly as the live
/// clients do, then assert the resulting JSON shape — so the per-provider contract
/// is locked headless, with no HTTP and no live model.
///
/// Two invariants run through every case:
///   1. Text-only messages stay byte-identical to the pre-vision behaviour.
///   2. With images, each provider gets precisely the structure its API expects.
/// </summary>
public class VisionPayloadTests
{
    // 'QUJD' is base64("ABC"); 'WFla' is base64("XYZ"). Raw base64 — never a data URL.
    private const string Png1 = "QUJD";
    private const string Jpg2 = "WFla";

    private static ChatMessage TextOnly(string content = "make a parametric tower") =>
        ChatMessage.User(content);

    private static ChatMessage OneImage(string content = "match this facade") =>
        ChatMessage.User(content, new List<ImageData>
        {
            new() { Base64 = Png1, MimeType = "image/png" }
        });

    private static ChatMessage TwoImages(string content = "blend these two refs") =>
        ChatMessage.User(content, new List<ImageData>
        {
            new() { Base64 = Png1, MimeType = "image/png" },
            new() { Base64 = Jpg2, MimeType = "image/jpeg" }
        });

    /// <summary>Serialize via the runtime type, mirroring how the clients serialize.</summary>
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    // ─────────────────────────────  OpenAI / OpenRouter  ─────────────────────────────

    [Fact]
    public void OpenAi_TextOnly_IsRawString()
    {
        var el = Json(VisionPayload.OpenAiContent(TextOnly()));

        Assert.Equal(JsonValueKind.String, el.ValueKind);
        Assert.Equal("make a parametric tower", el.GetString());
    }

    [Fact]
    public void OpenAi_WithImage_IsTextPartThenImageUrlPart()
    {
        var el = Json(VisionPayload.OpenAiContent(OneImage()));

        Assert.Equal(JsonValueKind.Array, el.ValueKind);
        Assert.Equal(2, el.GetArrayLength());

        Assert.Equal("text", el[0].GetProperty("type").GetString());
        Assert.Equal("match this facade", el[0].GetProperty("text").GetString());

        Assert.Equal("image_url", el[1].GetProperty("type").GetString());
        Assert.Equal(
            "data:image/png;base64,QUJD",
            el[1].GetProperty("image_url").GetProperty("url").GetString());
    }

    [Fact]
    public void OpenAi_EmptyText_OmitsTextPart()
    {
        var msg = ChatMessage.User("", new List<ImageData> { new() { Base64 = Png1, MimeType = "image/png" } });
        var el = Json(VisionPayload.OpenAiContent(msg));

        // No empty text part — just the single image_url part.
        Assert.Equal(1, el.GetArrayLength());
        Assert.Equal("image_url", el[0].GetProperty("type").GetString());
    }

    [Fact]
    public void OpenAi_TwoImages_KeepsOrderAfterText()
    {
        var el = Json(VisionPayload.OpenAiContent(TwoImages()));

        Assert.Equal(3, el.GetArrayLength());
        Assert.Equal("text", el[0].GetProperty("type").GetString());
        Assert.Equal("data:image/png;base64,QUJD", el[1].GetProperty("image_url").GetProperty("url").GetString());
        Assert.Equal("data:image/jpeg;base64,WFla", el[2].GetProperty("image_url").GetProperty("url").GetString());
    }

    // ──────────────────────────────────  Claude  ─────────────────────────────────────

    [Fact]
    public void Claude_TextOnly_IsRawString()
    {
        var el = Json(VisionPayload.ClaudeContent(TextOnly()));

        Assert.Equal(JsonValueKind.String, el.ValueKind);
        Assert.Equal("make a parametric tower", el.GetString());
    }

    [Fact]
    public void Claude_WithImage_PutsImageBlockBeforeText()
    {
        var el = Json(VisionPayload.ClaudeContent(OneImage()));

        Assert.Equal(JsonValueKind.Array, el.ValueKind);
        Assert.Equal(2, el.GetArrayLength());

        // Anthropic guidance: image block FIRST, then the text that refers to it.
        var img = el[0];
        Assert.Equal("image", img.GetProperty("type").GetString());
        var source = img.GetProperty("source");
        Assert.Equal("base64", source.GetProperty("type").GetString());
        Assert.Equal("image/png", source.GetProperty("media_type").GetString());
        Assert.Equal("QUJD", source.GetProperty("data").GetString());

        Assert.Equal("text", el[1].GetProperty("type").GetString());
        Assert.Equal("match this facade", el[1].GetProperty("text").GetString());
    }

    [Fact]
    public void Claude_WithImage_UsesRawBase64NotDataUrl()
    {
        var el = Json(VisionPayload.ClaudeContent(OneImage()));
        var data = el[0].GetProperty("source").GetProperty("data").GetString();

        Assert.Equal("QUJD", data);
        Assert.DoesNotContain("data:", data); // never a data URL on the Anthropic path
    }

    // ──────────────────────────────────  Gemini  ─────────────────────────────────────

    [Fact]
    public void Gemini_TextOnly_IsSingleTextPart()
    {
        var el = Json(VisionPayload.GeminiParts(TextOnly()));

        Assert.Equal(JsonValueKind.Array, el.ValueKind);
        Assert.Equal(1, el.GetArrayLength());
        Assert.Equal("make a parametric tower", el[0].GetProperty("text").GetString());
        Assert.False(el[0].TryGetProperty("inlineData", out _));
    }

    [Fact]
    public void Gemini_WithImage_TextPartThenInlineData()
    {
        var el = Json(VisionPayload.GeminiParts(OneImage()));

        Assert.Equal(2, el.GetArrayLength());
        Assert.Equal("match this facade", el[0].GetProperty("text").GetString());

        var inline = el[1].GetProperty("inlineData");
        Assert.Equal("image/png", inline.GetProperty("mimeType").GetString());
        Assert.Equal("QUJD", inline.GetProperty("data").GetString());
    }

    [Fact]
    public void Gemini_AlwaysKeepsTextPartEvenWhenEmpty()
    {
        var msg = ChatMessage.User("", new List<ImageData> { new() { Base64 = Png1, MimeType = "image/png" } });
        var el = Json(VisionPayload.GeminiParts(msg));

        // A part always exists; the (empty) text part is retained.
        Assert.Equal(2, el.GetArrayLength());
        Assert.Equal("", el[0].GetProperty("text").GetString());
        Assert.True(el[1].TryGetProperty("inlineData", out _));
    }

    // ──────────────────────────────────  Ollama  ─────────────────────────────────────

    [Fact]
    public void Ollama_TextOnly_ReturnsNull()
    {
        // Null lets the client omit the `images` field entirely → wire-identical to before.
        Assert.Null(VisionPayload.OllamaImages(TextOnly()));
    }

    [Fact]
    public void Ollama_WithImage_IsRawBase64Array()
    {
        var images = VisionPayload.OllamaImages(OneImage());

        Assert.NotNull(images);
        Assert.Single(images!);
        Assert.Equal("QUJD", images![0]);
        Assert.DoesNotContain("data:", images[0]); // no data-URL prefix on the Ollama path
    }

    [Fact]
    public void Ollama_TwoImages_PreservesOrder()
    {
        var images = VisionPayload.OllamaImages(TwoImages());

        Assert.Equal(new[] { "QUJD", "WFla" }, images);
    }
}
