using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CrystalRadio.Services;

public class IcyStreamInfo
{
    public HttpStatusCode StatusCode { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
    public int? IcyMetaInt { get; set; }
    public string? IcyName { get; set; }
    public string? IcyGenre { get; set; }
    public string? IcyBr { get; set; }
    public string? IcyUrl { get; set; }
    public string? IcyDescription { get; set; }
    public string? ContentType { get; set; }
    public string? StreamTitle { get; set; }
    public string? CurrentTrack { get; set; }
    public string? TrackUrl { get; set; }
    public string? RawMetadata { get; set; }
}

public class IcyMetadataService
{
    private const int DefaultTimeout = 10000;
    private const int MaxRedirects = 5;

    public async Task<IcyStreamInfo> GetIcyMetadataAsync(
        string streamUrl,
        int timeout = DefaultTimeout,
        CancellationToken cancellationToken = default)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = MaxRedirects
        };

        using var httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMilliseconds(timeout)
        };

        var request = new HttpRequestMessage(HttpMethod.Get, streamUrl);
        request.Headers.Add("Icy-MetaData", "1");
        request.Headers.Add("User-Agent", "CrystalRadio-ICY-Reader/1.0");

        try
        {
            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            Plugin.Log.Debug($"[ICY Service] Response status: {response.StatusCode}");

            var streamInfo = new IcyStreamInfo
            {
                StatusCode = response.StatusCode
            };

            foreach (var header in response.Headers)
                streamInfo.Headers[header.Key.ToLowerInvariant()] = string.Join(", ", header.Value);

            foreach (var header in response.Content.Headers)
                streamInfo.Headers[header.Key.ToLowerInvariant()] = string.Join(", ", header.Value);

            streamInfo.IcyName = GetHeaderValue(response, "icy-name");
            streamInfo.IcyGenre = GetHeaderValue(response, "icy-genre");
            streamInfo.IcyBr = GetHeaderValue(response, "icy-br");
            streamInfo.IcyUrl = GetHeaderValue(response, "icy-url");
            streamInfo.IcyDescription = GetHeaderValue(response, "icy-description");
            streamInfo.ContentType = response.Content.Headers.ContentType?.ToString();

            var metaIntString = GetHeaderValue(response, "icy-metaint");
            Plugin.Log.Debug($"[ICY Service] icy-metaint header: {metaIntString ?? "(not present)"}");

            if (!string.IsNullOrEmpty(metaIntString) && int.TryParse(metaIntString, out int metaInt))
            {
                streamInfo.IcyMetaInt = metaInt;
                Plugin.Log.Debug($"[ICY Service] Reading metadata blocks with interval: {metaInt}");
                await ReadMetadataFromStreamAsync(response, streamInfo, metaInt, cancellationToken);
                Plugin.Log.Debug($"[ICY Service] Finished reading, StreamTitle: {streamInfo.StreamTitle ?? "(none found)"}");
            }
            else
            {
                Plugin.Log.Debug("[ICY Service] Stream does not support ICY metadata");
            }

            return streamInfo;
        }
        catch (TaskCanceledException)
        {
            throw new TimeoutException("Request timeout while retrieving ICY metadata");
        }
        catch (Exception ex)
        {
            throw new Exception($"Error retrieving ICY metadata: {ex.Message}", ex);
        }
    }

    private string? GetHeaderValue(HttpResponseMessage response, string headerName)
    {
        if (response.Headers.TryGetValues(headerName, out var values))
            return string.Join(", ", values);

        if (response.Content.Headers.TryGetValues(headerName, out values))
            return string.Join(", ", values);

        return null;
    }

    private async Task ReadMetadataFromStreamAsync(
        HttpResponseMessage response,
        IcyStreamInfo streamInfo,
        int metaInt,
        CancellationToken cancellationToken)
    {
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        int bytesRead = 0;
        int blocksChecked = 0;
        const int maxBlocksToCheck = 10;

        try
        {
            while (!cancellationToken.IsCancellationRequested && blocksChecked < maxBlocksToCheck)
            {
                int audioDataToRead = metaInt - bytesRead;
                var audioBuffer = new byte[audioDataToRead];
                int totalRead = 0;

                while (totalRead < audioDataToRead)
                {
                    int read = await stream.ReadAsync(audioBuffer, totalRead, audioDataToRead - totalRead, cancellationToken);
                    if (read == 0)
                        return;

                    totalRead += read;
                }

                bytesRead = 0;

                var lengthBuffer = new byte[1];
                if (await stream.ReadAsync(lengthBuffer, 0, 1, cancellationToken) == 0)
                    return;

                int metadataLength = lengthBuffer[0] * 16;
                blocksChecked++;

                if (metadataLength > 0)
                {
                    var metadataBuffer = new byte[metadataLength];
                    totalRead = 0;

                    while (totalRead < metadataLength)
                    {
                        int read = await stream.ReadAsync(metadataBuffer, totalRead, metadataLength - totalRead, cancellationToken);
                        if (read == 0)
                            return;

                        totalRead += read;
                    }

                    var metadata = Encoding.UTF8.GetString(metadataBuffer).Replace("\0", "").Trim();
                    if (!string.IsNullOrEmpty(metadata))
                    {
                        streamInfo.RawMetadata = metadata;
                        Plugin.Log.Debug($"[ICY Service] Raw metadata: {metadata}");

                        var titleMatch = Regex.Match(
                            metadata,
                            @"StreamTitle='(.*?)';",
                            RegexOptions.Singleline);

                        if (titleMatch.Success)
                        {
                            var rawTitle = titleMatch.Groups[1].Value;
                            var cleanedTitle = SanitizeMetadataText(rawTitle);

                            streamInfo.StreamTitle = cleanedTitle;
                            streamInfo.CurrentTrack = cleanedTitle;

                            Plugin.Log.Debug($"[ICY Service] Extracted StreamTitle: {streamInfo.StreamTitle}");
                        }

                        var urlMatch = Regex.Match(metadata, @"StreamUrl='([^']*)';?");
                        if (urlMatch.Success)
                            streamInfo.TrackUrl = urlMatch.Groups[1].Value;

                        return;
                    }
                    else
                    {
                        Plugin.Log.Debug("[ICY Service] Empty metadata block");
                    }
                }
                else
                {
                    Plugin.Log.Debug($"[ICY Service] No metadata in block {blocksChecked}");
                }
            }

            Plugin.Log.Debug($"[ICY Service] Checked {blocksChecked} blocks, no metadata found");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[ICY Service] Error reading metadata from stream: {ex.Message}");
        }
    }

    private static string SanitizeMetadataText(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var decoded = WebUtility.HtmlDecode(input);

        var filtered = new string(decoded
            .Where(c => !char.IsControl(c) || c == '\t' || c == '\r' || c == '\n')
            .ToArray());

        filtered = filtered
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("\t", " ");

        while (filtered.Contains("  ", StringComparison.Ordinal))
            filtered = filtered.Replace("  ", " ", StringComparison.Ordinal);

        return filtered.Trim();
    }

    public async Task<bool> SupportsIcyMetadataAsync(
        string streamUrl,
        int timeout = DefaultTimeout,
        CancellationToken cancellationToken = default)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = MaxRedirects
        };

        using var httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMilliseconds(timeout)
        };

        var request = new HttpRequestMessage(HttpMethod.Get, streamUrl);
        request.Headers.Add("Icy-MetaData", "1");
        request.Headers.Add("User-Agent", "CrystalRadio-ICY-Reader/1.0");

        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var metaIntString = GetHeaderValue(response, "icy-metaint");
            return !string.IsNullOrEmpty(metaIntString) && int.TryParse(metaIntString, out _);
        }
        catch
        {
            return false;
        }
    }
}
