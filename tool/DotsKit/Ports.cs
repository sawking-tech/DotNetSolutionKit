namespace DotsKit;

/// <summary>
/// The port a service is generated with. The template picks one at random when none is named, so a base
/// generated again would differ from the solution; the tool picks it once, for a new service only, and keeps it
/// in the manifest.
/// </summary>
internal static class Ports
{
    private const int First = 5000;
    private const int Count = 1000;

    /// <summary>
    /// The service with its port: the one the command names, the one a service of the manifest already has, or,
    /// for a new service, the first port no service of the manifest has.
    /// </summary>
    public static TemplateArgs Assign(TemplateArgs service, Manifest? manifest)
    {
        if (service.Has(TemplateArgs.HttpPort) && service.Port is null)
        {
            throw new ArgumentException($"--{TemplateArgs.HttpPort} {service[TemplateArgs.HttpPort]} is not a port.");
        }

        var others = (manifest?.ServiceArgs ?? []).Where(s => !string.Equals(s.Service, service.Service, StringComparison.OrdinalIgnoreCase)).ToList();
        var taken = others.Select(s => s.Port).OfType<int>().ToHashSet();
        if (service.Port is { } named)
        {
            return taken.Contains(named) ? throw new ArgumentException($"Port {named} is taken by another service of the solution.") : service;
        }

        if (manifest?.FindService(service.Service) is not null)
        {
            return service;
        }

        var free = Enumerable.Range(First, Count).FirstOrDefault(p => !taken.Contains(p));
        if (free == 0)
        {
            throw new InvalidOperationException($"Every port from {First} to {First + Count - 1} is taken: name one with --{TemplateArgs.HttpPort}.");
        }

        return service.With(TemplateArgs.HttpPort, free.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
