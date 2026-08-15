using System.Globalization;
using AutVecu.Cores.Enums;

namespace AutVecu.Cores.Diagnostics;

public static class UdsProtocol
{
    public const byte PositiveResponseOffset = 0x40;
    public const byte NegativeResponseServiceId = 0x7F;
    public const byte SuppressPositiveResponseMask = 0x80;

    public static string BuildRequestHex(UdsServiceId serviceId, params byte[] parameters)
    {
        return ToHex([(byte)serviceId, .. parameters]);
    }

    public static string BuildPositiveResponsePrefixHex(UdsServiceId serviceId, params byte[] parameters)
    {
        return ToHex([GetPositiveResponseServiceId(serviceId), .. parameters]);
    }

    public static string BuildReadDataByIdentifierRequestHex(UdsDataIdentifier dataIdentifier)
    {
        return BuildRequestHex(UdsServiceId.ReadDataByIdentifier, ToBigEndianBytes((ushort)dataIdentifier));
    }

    public static string BuildReadDataByIdentifierPositivePrefixHex(UdsDataIdentifier dataIdentifier)
    {
        return BuildPositiveResponsePrefixHex(UdsServiceId.ReadDataByIdentifier, ToBigEndianBytes((ushort)dataIdentifier));
    }

    public static byte GetPositiveResponseServiceId(UdsServiceId serviceId)
    {
        return (byte)((byte)serviceId + PositiveResponseOffset);
    }

    public static bool IsNegativeResponse(IReadOnlyList<byte> payload)
    {
        return payload.Count >= 3 && payload[0] == NegativeResponseServiceId;
    }

    public static bool IsServiceRequest(IReadOnlyList<byte> payload, UdsServiceId serviceId)
    {
        return payload.Count > 0 && payload[0] == (byte)serviceId;
    }

    public static bool IsPositiveResponse(IReadOnlyList<byte> payload, UdsServiceId serviceId)
    {
        return payload.Count > 0 && payload[0] == GetPositiveResponseServiceId(serviceId);
    }

    public static bool StartsWith(IReadOnlyList<byte> payload, params byte[] prefix)
    {
        if (payload.Count < prefix.Length)
        {
            return false;
        }

        for (var index = 0; index < prefix.Length; index++)
        {
            if (payload[index] != prefix[index])
            {
                return false;
            }
        }

        return true;
    }

    public static bool TryReadDataIdentifier(IReadOnlyList<byte> payload, int offset, out UdsDataIdentifier dataIdentifier)
    {
        dataIdentifier = default;
        if (payload.Count <= offset + 1)
        {
            return false;
        }

        var value = (ushort)((payload[offset] << 8) | payload[offset + 1]);

        dataIdentifier = (UdsDataIdentifier)value;
        return true;
    }

    public static bool TryParseHexPayload(string payload, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var parsedBytes = new List<byte>();
        foreach (var token in payload.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!byte.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
            {
                return false;
            }

            parsedBytes.Add(parsed);
        }

        bytes = [.. parsedBytes];
        return true;
    }

    public static string ToHex(params byte[] bytes)
    {
        return string.Join(' ', bytes.Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));
    }

    public static string GetServiceName(byte serviceId)
    {
        return Enum.IsDefined(typeof(UdsServiceId), serviceId)
            ? ((UdsServiceId)serviceId).ToString()
            : "Unknown service";
    }

    public static string GetNegativeResponseCodeName(byte nrc)
    {
        return Enum.IsDefined(typeof(UdsNegativeResponseCode), nrc)
            ? ((UdsNegativeResponseCode)nrc).ToString()
            : "Unknown NRC";
    }

    public static string GetDataIdentifierName(UdsDataIdentifier dataIdentifier)
    {
        return dataIdentifier switch
        {
            UdsDataIdentifier.VehicleSpeed => "Vehicle speed",
            UdsDataIdentifier.EngineRpm => "Engine RPM",
            UdsDataIdentifier.CoolantTemperature => "Coolant temperature",
            UdsDataIdentifier.CoolantTemperatureFaultInjection => "Coolant temperature fault injection",
            UdsDataIdentifier.Vin => "VIN",
            UdsDataIdentifier.VehicleManufacturerSparePartNumber => "Vehicle manufacturer spare part number",
            UdsDataIdentifier.VehicleManufacturerEcuSoftwareNumber => "Vehicle manufacturer ECU software number",
            UdsDataIdentifier.EcuSerialNumber => "ECU serial number",
            _ => $"DID 0x{(ushort)dataIdentifier:X4}"
        };
    }

    private static byte[] ToBigEndianBytes(ushort value)
    {
        return [(byte)(value >> 8), (byte)(value & 0xFF)];
    }
}
