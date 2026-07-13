using System.Globalization;
using ElectricityExplorer.Core.Models;

namespace ElectricityExplorer.Core.Nem12;

public sealed class Nem12Parser
{
    public async Task<ElectricityDataset> ParseAsync(
        Stream stream,
        string sourceFileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead)
        {
            throw new ArgumentException("The NEM12 stream must be readable.", nameof(stream));
        }

        var dataset = new ElectricityDataset
        {
            Name = GetDatasetName(sourceFileName),
            SourceFileName = sourceFileName,
            ImportedAt = DateTimeOffset.UtcNow
        };

        var channelsByKey = new Dictionary<string, Nem12Channel>(StringComparer.OrdinalIgnoreCase);
        var readingsByChannel = new Dictionary<string, ChannelReadingBuffer>(StringComparer.Ordinal);
        var warningKeys = new HashSet<string>(StringComparer.Ordinal);

        using var reader = new StreamReader(stream, leaveOpen: true);

        Nem12Channel? currentChannel = null;
        var sawHeader = false;
        var sawEndRecord = false;
        var lineNumber = 0;

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var recordIndicator = GetRecordIndicator(line, lineNumber);

            switch (recordIndicator)
            {
                case "100":
                    var headerFields = ParseFields(line, lineNumber);
                    ValidateHeader(headerFields, lineNumber);
                    sawHeader = true;
                    sawEndRecord = false;
                    currentChannel = null;
                    break;

                case "200":
                    var channelFields = ParseFields(line, lineNumber);
                    if (!sawHeader)
                    {
                        sawHeader = true;
                        AddWarning(
                            dataset,
                            warningKeys,
                            "missing-header",
                            "The file omitted the standard 100 header record. Import started from the first 200 channel record.");
                    }

                    currentChannel = ParseChannel(
                        channelFields,
                        lineNumber,
                        dataset,
                        channelsByKey,
                        warningKeys);
                    break;

                case "300":
                    EnsureHeader(sawHeader, lineNumber);
                    if (currentChannel is null)
                    {
                        throw new Nem12ParseException(
                            lineNumber,
                            "An interval data record appeared before its 200 channel record.");
                    }

                    ParseIntervalData(line, lineNumber, currentChannel, readingsByChannel);
                    break;

                case "400":
                case "500":
                    EnsureHeader(sawHeader, lineNumber);
                    break;

                case "900":
                    EnsureHeader(sawHeader, lineNumber);
                    sawEndRecord = true;
                    currentChannel = null;
                    break;

                default:
                    AddWarning(
                        dataset,
                        warningKeys,
                        $"record:{recordIndicator}",
                        $"Record type {recordIndicator} was ignored.");
                    break;
            }
        }

        if (!sawHeader)
        {
            throw new Nem12ParseException(Math.Max(lineNumber, 1), "No NEM12 100 header record was found.");
        }

        if (!sawEndRecord)
        {
            AddWarning(
                dataset,
                warningKeys,
                "missing-end",
                "The file did not finish with a 900 end record.");
        }

        dataset.Channels = channelsByKey.Values
            .OrderBy(channel => channel.Nmi, StringComparer.OrdinalIgnoreCase)
            .ThenBy(channel => channel.NmiSuffix, StringComparer.OrdinalIgnoreCase)
            .ToList();

        dataset.Readings = readingsByChannel.Values
            .SelectMany(buffer => buffer.Readings)
            .ToList();

        if (dataset.Channels.Count == 0)
        {
            throw new Nem12ParseException(Math.Max(lineNumber, 1), "No NEM12 200 channel records were found.");
        }

        if (dataset.Readings.Count == 0)
        {
            throw new Nem12ParseException(
                Math.Max(lineNumber, 1),
                "No supported active-energy interval readings were found.");
        }

        return dataset;
    }

    private static void ValidateHeader(IReadOnlyList<string> fields, int lineNumber)
    {
        var format = GetField(fields, 1, lineNumber, "version header");
        if (!string.Equals(format.Trim(), "NEM12", StringComparison.OrdinalIgnoreCase))
        {
            throw new Nem12ParseException(
                lineNumber,
                $"Expected a NEM12 header but found '{format}'.");
        }
    }

    private static Nem12Channel ParseChannel(
        IReadOnlyList<string> fields,
        int lineNumber,
        ElectricityDataset dataset,
        IDictionary<string, Nem12Channel> channelsByKey,
        ISet<string> warningKeys)
    {
        if (fields.Count < 9)
        {
            throw new Nem12ParseException(
                lineNumber,
                $"A 200 record requires at least 9 fields but only {fields.Count} were present.");
        }

        var nmi = GetField(fields, 1, lineNumber, "NMI").Trim();
        var nmiConfiguration = GetField(fields, 2, lineNumber, "NMI configuration").Trim();
        var registerId = OptionalField(fields, 3);
        var nmiSuffix = GetField(fields, 4, lineNumber, "NMI suffix").Trim();
        var dataStreamIdentifier = OptionalField(fields, 5);
        var meterSerialNumber = OptionalField(fields, 6);
        var unit = GetField(fields, 7, lineNumber, "unit of measure").Trim();
        var intervalText = GetField(fields, 8, lineNumber, "interval length").Trim();

        if (string.IsNullOrWhiteSpace(nmi))
        {
            throw new Nem12ParseException(lineNumber, "The NMI in a 200 record cannot be empty.");
        }

        if (!int.TryParse(intervalText, NumberStyles.None, CultureInfo.InvariantCulture, out var intervalMinutes)
            || intervalMinutes <= 0
            || 1440 % intervalMinutes != 0)
        {
            throw new Nem12ParseException(
                lineNumber,
                $"'{intervalText}' is not a supported interval length in minutes.");
        }

        var key = string.Join(
            "|",
            nmi,
            nmiSuffix,
            registerId ?? string.Empty,
            dataStreamIdentifier ?? string.Empty,
            meterSerialNumber ?? string.Empty,
            intervalMinutes.ToString(CultureInfo.InvariantCulture));

        if (channelsByKey.TryGetValue(key, out var existingChannel))
        {
            return existingChannel;
        }

        var direction = InferDirection(nmiSuffix, nmiConfiguration, dataStreamIdentifier);
        if (!TryGetKwhFactor(unit, out _))
        {
            direction = EnergyFlowDirection.Ignore;
            AddWarning(
                dataset,
                warningKeys,
                $"unit:{unit}",
                $"Channels measured in '{unit}' were retained as metadata but ignored because only Wh, kWh and MWh are supported.");
        }
        else if (direction == EnergyFlowDirection.Ignore)
        {
            AddWarning(
                dataset,
                warningKeys,
                $"direction:{nmi}:{nmiSuffix}",
                $"Channel {nmiSuffix} for NMI {nmi} could not be classified automatically. Map it before running an analysis.");
        }

        var channel = new Nem12Channel
        {
            Id = (channelsByKey.Count + 1).ToString(CultureInfo.InvariantCulture),
            Nmi = nmi,
            NmiConfiguration = nmiConfiguration,
            RegisterId = registerId,
            NmiSuffix = nmiSuffix,
            DataStreamIdentifier = dataStreamIdentifier,
            MeterSerialNumber = meterSerialNumber,
            Unit = unit,
            IntervalMinutes = intervalMinutes,
            Direction = direction
        };

        channelsByKey.Add(key, channel);
        return channel;
    }

    private static void ParseIntervalData(
        string line,
        int lineNumber,
        Nem12Channel channel,
        IDictionary<string, ChannelReadingBuffer> readingsByChannel)
    {
        var expectedIntervalCount = 1440 / channel.IntervalMinutes;
        var remaining = line.AsSpan();
        _ = TryReadField(ref remaining, out _);

        if (!TryReadField(ref remaining, out var dateText))
        {
            throw new Nem12ParseException(lineNumber, "The interval date field is missing.");
        }

        dateText = dateText.Trim();
        if (!DateOnly.TryParseExact(
                dateText,
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            throw new Nem12ParseException(
                lineNumber,
                $"'{dateText.ToString()}' is not a valid NEM12 interval date.");
        }

        if (!TryGetKwhFactor(channel.Unit, out var kwhFactor))
        {
            return;
        }

        if (!readingsByChannel.TryGetValue(channel.Id, out var buffer))
        {
            buffer = new ChannelReadingBuffer();
            readingsByChannel.Add(channel.Id, buffer);
        }

        var replacingExistingDay = buffer.DayOffsets.TryGetValue(date, out var dayOffset);
        if (!replacingExistingDay)
        {
            dayOffset = buffer.Readings.Count;
            buffer.DayOffsets.Add(date, dayOffset);
            buffer.Readings.EnsureCapacity(buffer.Readings.Count + expectedIntervalCount);
        }

        var startOfDay = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        for (var intervalIndex = 0; intervalIndex < expectedIntervalCount; intervalIndex++)
        {
            if (!TryReadField(ref remaining, out var valueText))
            {
                throw new Nem12ParseException(
                    lineNumber,
                    $"A {channel.IntervalMinutes}-minute 300 record requires {expectedIntervalCount} interval values.");
            }

            valueText = valueText.Trim();
            if (!TryParseEnergy(valueText, out var value))
            {
                throw new Nem12ParseException(
                    lineNumber,
                    $"Interval {intervalIndex + 1} contains the invalid energy value '{valueText.ToString()}'.");
            }

            var timestamp = startOfDay.AddMinutes(intervalIndex * channel.IntervalMinutes);
            var reading = new Nem12Reading
            {
                ChannelId = channel.Id,
                Timestamp = timestamp,
                EnergyKwh = value * kwhFactor
            };

            if (replacingExistingDay)
            {
                buffer.Readings[dayOffset + intervalIndex] = reading;
            }
            else
            {
                buffer.Readings.Add(reading);
            }
        }
    }

    private static EnergyFlowDirection InferDirection(
        string nmiSuffix,
        string nmiConfiguration,
        string? dataStreamIdentifier)
    {
        var suffix = nmiSuffix.Trim().ToUpperInvariant();
        if (suffix.StartsWith('E'))
        {
            return EnergyFlowDirection.Import;
        }

        if (suffix.StartsWith('B'))
        {
            return EnergyFlowDirection.Export;
        }

        var description = $"{nmiConfiguration} {dataStreamIdentifier}".ToUpperInvariant();
        if (description.Contains("IMPORT", StringComparison.Ordinal)
            || description.Contains("CONSUMPTION", StringComparison.Ordinal)
            || description.Contains("GENERAL", StringComparison.Ordinal))
        {
            return EnergyFlowDirection.Import;
        }

        if (description.Contains("EXPORT", StringComparison.Ordinal)
            || description.Contains("GENERATION", StringComparison.Ordinal)
            || description.Contains("SOLAR", StringComparison.Ordinal))
        {
            return EnergyFlowDirection.Export;
        }

        return EnergyFlowDirection.Ignore;
    }

    private static bool TryGetKwhFactor(string unit, out double factor)
    {
        var normalizedUnit = unit.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        switch (normalizedUnit)
        {
            case "WH":
                factor = 0.001;
                return true;
            case "KWH":
                factor = 1;
                return true;
            case "MWH":
                factor = 1000;
                return true;
            default:
                factor = 0;
                return false;
        }
    }

    private static bool TryParseEnergy(ReadOnlySpan<char> text, out double value)
    {
        if (text.IsEmpty)
        {
            value = 0;
            return false;
        }

        var index = 0;
        var negative = false;
        if (text[0] is '-' or '+')
        {
            negative = text[0] == '-';
            index++;
        }

        var parsedValue = 0d;
        var decimalScale = 0.1d;
        var decimalPointSeen = false;
        var digitSeen = false;

        for (; index < text.Length; index++)
        {
            var character = text[index];
            if (character is >= '0' and <= '9')
            {
                digitSeen = true;
                var digit = character - '0';
                if (decimalPointSeen)
                {
                    parsedValue += digit * decimalScale;
                    decimalScale *= 0.1d;
                }
                else
                {
                    parsedValue = parsedValue * 10d + digit;
                }

                continue;
            }

            if (character == '.' && !decimalPointSeen)
            {
                decimalPointSeen = true;
                continue;
            }

            return double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
        }

        value = negative ? -parsedValue : parsedValue;
        return digitSeen;
    }

    private static string GetDatasetName(string sourceFileName)
    {
        var name = Path.GetFileNameWithoutExtension(sourceFileName);
        return string.IsNullOrWhiteSpace(name) ? "NEM12 import" : name;
    }

    private static void EnsureHeader(bool sawHeader, int lineNumber)
    {
        if (!sawHeader)
        {
            throw new Nem12ParseException(
                lineNumber,
                "A data record appeared before the NEM12 100 header record.");
        }
    }

    private static string GetRecordIndicator(string line, int lineNumber)
    {
        var separatorIndex = line.IndexOf(',');
        var indicator = separatorIndex < 0
            ? line.AsSpan()
            : line.AsSpan(0, separatorIndex);
        indicator = indicator.Trim().TrimStart('\uFEFF').Trim('"');

        if (indicator.IsEmpty)
        {
            throw new Nem12ParseException(lineNumber, "The record indicator field is missing.");
        }

        return indicator.ToString();
    }

    private static IReadOnlyList<string> ParseFields(string line, int lineNumber)
    {
        try
        {
            return CsvLineParser.Parse(line);
        }
        catch (FormatException exception)
        {
            throw new Nem12ParseException(lineNumber, exception.Message, exception);
        }
    }

    private static bool TryReadField(
        ref ReadOnlySpan<char> remaining,
        out ReadOnlySpan<char> field)
    {
        if (remaining.IsEmpty)
        {
            field = default;
            return false;
        }

        var separatorIndex = remaining.IndexOf(',');
        if (separatorIndex < 0)
        {
            field = remaining;
            remaining = default;
            return true;
        }

        field = remaining[..separatorIndex];
        remaining = remaining[(separatorIndex + 1)..];
        return true;
    }

    private static string GetField(
        IReadOnlyList<string> fields,
        int index,
        int lineNumber,
        string fieldName)
    {
        if (index >= fields.Count)
        {
            throw new Nem12ParseException(lineNumber, $"The {fieldName} field is missing.");
        }

        return fields[index];
    }

    private static string? OptionalField(IReadOnlyList<string> fields, int index)
    {
        if (index >= fields.Count)
        {
            return null;
        }

        var value = fields[index].Trim();
        return value.Length == 0 ? null : value;
    }

    private static void AddWarning(
        ElectricityDataset dataset,
        ISet<string> warningKeys,
        string key,
        string warning)
    {
        if (warningKeys.Add(key))
        {
            dataset.Warnings.Add(warning);
        }
    }

}
