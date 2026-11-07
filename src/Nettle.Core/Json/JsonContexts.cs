using System.Text.Json.Serialization;
using Nettle.Core.Models;

namespace Nettle.Core.Json;

// Source-generated serializers: fast startup, no reflection, trimming/AOT friendly (iOS-ready).

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PublicSystemInfo))]
[JsonSerializable(typeof(UserDto))]
[JsonSerializable(typeof(List<UserDto>))]
[JsonSerializable(typeof(AuthenticateUserByName))]
[JsonSerializable(typeof(AuthenticationResult))]
[JsonSerializable(typeof(QuickConnectResult))]
[JsonSerializable(typeof(QuickConnectDto))]
[JsonSerializable(typeof(BaseItemDto))]
[JsonSerializable(typeof(List<BaseItemDto>))]
[JsonSerializable(typeof(QueryResult<BaseItemDto>))]
[JsonSerializable(typeof(QueryResult<MediaSegmentDto>))]
[JsonSerializable(typeof(Dictionary<string, IntroSkipperSegment>))]
[JsonSerializable(typeof(PlaybackInfoDto))]
[JsonSerializable(typeof(PlaybackInfoResponse))]
[JsonSerializable(typeof(PlaybackProgressInfo))]
[JsonSerializable(typeof(List<PluginInfo>))]
[JsonSerializable(typeof(DiscoveredServer))]
[JsonSerializable(typeof(object))]
public partial class JellyfinJson : JsonSerializerContext { }

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SeerrPage))]
[JsonSerializable(typeof(SeerrDetails))]
[JsonSerializable(typeof(SeerrRequestPage))]
[JsonSerializable(typeof(SeerrRequest))]
[JsonSerializable(typeof(SeerrCreateRequest))]
[JsonSerializable(typeof(SeerrJellyfinLogin))]
[JsonSerializable(typeof(SeerrUser))]
public partial class SeerrJson : JsonSerializerContext { }

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(NtvScheduleResponse))]
public partial class NtvJson : JsonSerializerContext { }

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(AccountVault))]
public partial class LocalJson : JsonSerializerContext { }
