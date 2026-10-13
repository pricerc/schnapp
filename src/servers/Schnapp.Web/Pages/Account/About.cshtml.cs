using System.Reflection;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Schnapp.Web.Pages.Account;

/// <summary>Basic product / build information (expand later).</summary>
public sealed class AboutModel : PageModel
{
    public string AppName { get; private set; } = "Schnapp";
    public string Version { get; private set; } = "—";
    public string Framework { get; private set; } = "—";

    public void OnGet()
    {
        var asm = typeof(AboutModel).Assembly;
        AppName = asm.GetCustomAttribute<AssemblyProductAttribute>()?.Product
            ?? asm.GetName().Name
            ?? "Schnapp";
        Version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? asm.GetName().Version?.ToString()
            ?? "—";
        Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
    }
}
