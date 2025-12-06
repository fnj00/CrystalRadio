using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CrystalRadio.Services;

/// <summary>
/// Information about an ICY stream and its metadata
/// </summary>
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

/// <summary>
/// Service for retrieving ICY metadata from streaming audio URLs
/// </summary>
public class IcyMetadataService
{
    private const int DefaultTimeout = 10000; // 10 seconds
    private const int MaxRedirects = 5;

    /// <summary>
    /// Retrieves ICY metadata from a streaming URL
    /// </summary>
    /// <param name="streamUrl">The URL of the audio stream</param>
    /// <param name="timeout">Request timeout in milliseconds (default: 10000)</param>
    /// <param name="cancellationToken">Optional cancellation token</param>
    /// <returns>Stream information including metadata</returns>
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

        // Request ICY metadata
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

            // Extract headers
            foreach (var header in response.Headers)
            {
                streamInfo.Headers[header.Key.ToLower()] = string.Join(", ", header.Value);
            }

            foreach (var header in response.Content.Headers)
            {
                streamInfo.Headers[header.Key.ToLower()] = string.Join(", ", header.Value);
            }

            // Extract ICY-specific headers
            streamInfo.IcyName = GetHeaderValue(response, "icy-name");
            streamInfo.IcyGenre = GetHeaderValue(response, "icy-genre");
            streamInfo.IcyBr = GetHeaderValue(response, "icy-br");
            streamInfo.IcyUrl = GetHeaderValue(response, "icy-url");
            streamInfo.IcyDescription = GetHeaderValue(response, "icy-description");
            streamInfo.ContentType = response.Content.Headers.ContentType?.ToString();

            // Get metadata interval
            var metaIntString = GetHeaderValue(response, "icy-metaint");
            Plugin.Log.Debug($"[ICY Service] icy-metaint header: {metaIntString ?? "(not present)"}");
            
            if (!string.IsNullOrEmpty(metaIntString) && int.TryParse(metaIntString, out int metaInt))
            {
                streamInfo.IcyMetaInt = metaInt;
                Plugin.Log.Debug($"[ICY Service] Reading metadata blocks with interval: {metaInt}");

                // Read stream data to extract metadata - this will read until it finds metadata
                await ReadMetadataFromStreamAsync(response, streamInfo, metaInt, cancellationToken);
                
                Plugin.Log.Debug($"[ICY Service] Finished reading, StreamTitle: {streamInfo.StreamTitle ?? "(none found)"}");
            }
            else
            {
                Plugin.Log.Debug($"[ICY Service] Stream does not support ICY metadata");
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
        // Check response headers
        if (response.Headers.TryGetValues(headerName, out var values))
        {
            return string.Join(", ", values);
        }

        // Check content headers
        if (response.Content.Headers.TryGetValues(headerName, out values))
        {
            return string.Join(", ", values);
        }

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
        const int maxBlocksToCheck = 10; // Check up to 10 blocks to find metadata

        try
        {
            // Continue reading until we find metadata or reach max blocks
            while (!cancellationToken.IsCancellationRequested && blocksChecked < maxBlocksToCheck)
            {
                // Read audio data up to the metadata block
                int audioDataToRead = metaInt - bytesRead;
                var audioBuffer = new byte[audioDataToRead];
                int totalRead = 0;

                while (totalRead < audioDataToRead)
                {
                    int read = await stream.ReadAsync(audioBuffer, totalRead, audioDataToRead - totalRead, cancellationToken);
                    if (read == 0)
                        return; // End of stream
                    
                    totalRead += read;
                }

                bytesRead = 0; // Reset for next block

                // Read metadata length byte
                var lengthBuffer = new byte[1];
                if (await stream.ReadAsync(lengthBuffer, 0, 1, cancellationToken) == 0)
                    return; // End of stream

                int metadataLength = lengthBuffer[0] * 16;

                blocksChecked++;

                if (metadataLength > 0)
                {
                    // Read metadata block
                    var metadataBuffer = new byte[metadataLength];
                    totalRead = 0;

                    while (totalRead < metadataLength)
                    {
                        int read = await stream.ReadAsync(metadataBuffer, totalRead, metadataLength - totalRead, cancellationToken);
                        if (read == 0)
                            return; // End of stream
                        
                        totalRead += read;
                    }

                    // Parse metadata
                    var metadata = Encoding.UTF8.GetString(metadataBuffer).Replace("\0", "").Trim();

                    if (!string.IsNullOrEmpty(metadata))
                    {
                        streamInfo.RawMetadata = metadata;
                        Plugin.Log.Debug($"[ICY Service] Raw metadata: {metadata}");

                        // Parse StreamTitle and StreamUrl
                        var titleMatch = Regex.Match(metadata, @"StreamTitle='([^']*)';?");
                        if (titleMatch.Success)
                        {
                            streamInfo.StreamTitle = titleMatch.Groups[1].Value;
                            streamInfo.CurrentTrack = titleMatch.Groups[1].Value; // For backward compatibility
                            Plugin.Log.Debug($"[ICY Service] Extracted StreamTitle: {streamInfo.StreamTitle}");
                        }

                        var urlMatch = Regex.Match(metadata, @"StreamUrl='([^']*)';?");
                        if (urlMatch.Success)
                        {
                            streamInfo.TrackUrl = urlMatch.Groups[1].Value;
                        }
                        
                        // Found metadata, exit
                        return;
                    }
                    else
                    {
                        Plugin.Log.Debug($"[ICY Service] Empty metadata block");
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
            // Cancellation requested - this is expected
        }
        catch (Exception ex)
        {
            // If we fail to read metadata, just return what we have
            Plugin.Log.Error($"[ICY Service] Error reading metadata from stream: {ex.Message}");
        }
    }

    /// <summary>
    /// Quick check to see if a stream supports ICY metadata (only checks headers)
    /// </summary>
    /// <param name="streamUrl">The URL of the audio stream</param>
    /// <param name="timeout">Request timeout in milliseconds</param>
    /// <param name="cancellationToken">Optional cancellation token</param>
    /// <returns>True if the stream supports ICY metadata</returns>
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

