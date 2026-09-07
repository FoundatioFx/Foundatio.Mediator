using System.Text.Json;

namespace DistributedBenchmarks;

public sealed record Settings
{
    public string Framework { get; init; } = "foundatio";
    public string Transport { get; init; } = "memory";
    public string Operation { get; init; } = "queue";
    public int Count { get; init; } = 10_000;
    public int Warmup { get; init; } = 200;
    public int PayloadBytes { get; init; } = 128;
    public int Producers { get; init; } = 8;
    public int Concurrency { get; init; } = 8;
    public int Fanout { get; init; } = 1;
    public int Capacity { get; init; } = 1000;
    public int Rate { get; init; }
    public int WorkMicroseconds { get; init; }
    public int TimeoutSeconds { get; init; } = 180;
    public bool AllowDrops { get; init; }
    public string? ServiceUrl { get; init; } = "http://localhost:4566";
    public string Region { get; init; } = "us-east-1";
    public string RunId { get; init; } = "fmb-" + Guid.NewGuid().ToString("N")[..16];
    public string Queue => RunId + "-work";
    public string Topic => RunId + "-events";
    public bool Broker => Transport == "sqs";
    public int Subscribers => Operation == "pubsub" ? Fanout : 1;
    public string Label => $"{Framework}/{Transport}/{Operation}/p{PayloadBytes}/s{Producers}/c{Concurrency}/f{Fanout}/b{Capacity}/r{Rate}/w{WorkMicroseconds}";

    public void Validate()
    {
        if (Framework is not ("foundatio" or "masstransit") || Transport is not ("local" or "memory" or "sqs") || Operation is not ("queue" or "pubsub"))
            throw new ArgumentException("Use --framework foundatio|masstransit, --transport local|memory|sqs, --operation queue|pubsub.");
        if (Framework == "masstransit" && Transport == "local") throw new ArgumentException("The local baseline uses Foundatio's mediator.");
        if (Count < 1 || Count > 10_000_000 || Warmup < 1 || Warmup > 10_000_000 || PayloadBytes < 1 || PayloadBytes > 65_536 || Producers < 1 || Producers > 1024 || Concurrency < 1 || Concurrency > 1024 || Fanout < 1 || Fanout > 16 || Capacity < 1 || Capacity > 10_000_000 || TimeoutSeconds < 1 || TimeoutSeconds > 3600 || Rate < 0 || WorkMicroseconds < 0 || WorkMicroseconds > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(Settings), "Invalid workload limits.");
        if (ServiceUrl is not null && (!Uri.TryCreate(ServiceUrl, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https") || endpoint.UserInfo.Length > 0))
            throw new ArgumentException("The broker endpoint must be an HTTP(S) URL without embedded credentials.");
        if ((Operation == "queue" || Transport == "local") && Fanout != 1) throw new ArgumentException("Queue and local baselines require --fanout 1.");
        if (Operation == "pubsub" && Concurrency != 10 && Transport != "local")
            throw new ArgumentException("Use --concurrency 10 for pub/sub: Foundatio's SNS/SQS receiver processes one batch of up to ten concurrently.");
        if (!RunId.StartsWith("fmb-", StringComparison.Ordinal) || RunId.Length != 20 || RunId[4..].Any(c => !char.IsAsciiHexDigit(c)))
            throw new ArgumentException("RunId must be a harness-generated resource prefix.");
    }

    public static Settings Parse(string[] args)
    {
        var values = Arguments.Parse(args);
        if (values.ContainsKey("aws") && values.ContainsKey("endpoint")) throw new ArgumentException("Choose --aws or --endpoint, not both.");
        string Get(string key, string fallback) => values.GetValueOrDefault(key, fallback);
        int Number(string key, int fallback) => int.Parse(Get(key, fallback.ToString(System.Globalization.CultureInfo.InvariantCulture)), System.Globalization.CultureInfo.InvariantCulture);
        var operation = Get("operation", "queue");
        var result = new Settings
        {
            Framework = Get("framework", "foundatio"), Transport = Get("transport", "memory"), Operation = operation,
            Count = Number("count", 10_000), Warmup = Number("warmup", 200), PayloadBytes = Number("payload", 128),
            Producers = Number("producers", 8), Concurrency = Number("concurrency", operation == "pubsub" ? 10 : 8),
            Fanout = Number("fanout", 1), Capacity = Number("capacity", 1000), Rate = Number("rate", 0),
            WorkMicroseconds = Number("work-us", 0), TimeoutSeconds = Number("timeout", 180),
            AllowDrops = values.ContainsKey("allow-drops"), ServiceUrl = values.ContainsKey("aws") ? null : Get("endpoint", "http://localhost:4566"), Region = Get("region", "us-east-1")
        };
        result.Validate();
        return result;
    }
}

internal static class Arguments
{
    public static Dictionary<string, string> Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Expected an option, received '{args[i]}'.");
            string key = args[i][2..];
            if (!Known.Contains(key)) throw new ArgumentException($"Unknown option --{key}.");
            string value = key is "aws" or "allow-drops" ? "true" : ++i < args.Length ? args[i] : throw new ArgumentException($"Missing value for --{key}.");
            if (!values.TryAdd(key, value)) throw new ArgumentException($"Duplicate option --{key}.");
        }
        return values;
    }

    private static readonly HashSet<string> Known = ["framework", "transport", "operation", "count", "local-count", "memory-count", "warmup", "payload", "producers", "concurrency", "fanout", "capacity", "rate", "work-us", "timeout", "allow-drops", "aws", "endpoint", "region", "output", "repetitions", "suite", "seed"];
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}
