using System.Runtime.CompilerServices;
using DiffEngine;

namespace Foundatio.Mediator.Tests;

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        DiffTools.UseOrder(DiffTool.VisualStudioCode, DiffTool.Rider, DiffTool.VisualStudio);
    }
}
