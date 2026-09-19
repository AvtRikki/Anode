using Anode.Kicad;

namespace Anode.Render;

/// <summary>
/// What the drawing sheet prints that the sheet file cannot say about itself: the file it is read from, where it
/// stands in the design, and which page of how many. The document knows these; the title block fields come from
/// the file.
/// </summary>
/// <param name="SheetPath">The place in the design as KiCad prints it: "/" for the root, "/amplifier/" below it.</param>
/// <param name="Application">Printed where KiCad prints its own version.</param>
public sealed record SheetFrameText(
    string FileName = "",
    string SheetPath = "/",
    int Page = 1,
    int PageCount = 1,
    string Application = "Anode")
{
    /// <summary>The drawing sheet to draw; null for KiCad's default.</summary>
    public Kicad.DrawingSheets.DrawingSheetFile? Template { get; init; }

    /// <summary>The project's text variables, for <c>${NAME}</c> in the drawing sheet and the title block.</summary>
    public IReadOnlyDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>();

    /// <summary>The sheet's own name, for <c>${SHEETNAME}</c>.</summary>
    public string SheetName { get; init; } = string.Empty;

    /// <summary>
    /// What the project around a file says the frame should be: its drawing sheet for schematics or for boards, and
    /// its variables with <c>${PROJECTNAME}</c> among them. A sheet the project names but that is missing or will not
    /// read falls back to the default, and <paramref name="problem"/> says why.
    /// </summary>
    public static SheetFrameText ForProject(string file, bool board, out string? problem)
    {
        problem = null;
        var project = ProjectFile.For(file);
        var frame = new SheetFrameText(System.IO.Path.GetFileName(file), board ? string.Empty : "/");
        if (project is null)
        {
            return frame;
        }

        var variables = new Dictionary<string, string>(project.TextVariables) { ["PROJECTNAME"] = project.Name };
        Kicad.DrawingSheets.DrawingSheetFile? template = null;
        if (project.Resolve(board ? project.BoardDrawingSheet : project.SchematicDrawingSheet) is { } path)
        {
            try
            {
                template = Kicad.DrawingSheets.DrawingSheetFile.Load(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or KiCadFormatException or Anode.Sexpr.SexprParseException)
            {
                problem = path;
            }
        }

        return frame with { Template = template, Variables = variables };
    }
}
