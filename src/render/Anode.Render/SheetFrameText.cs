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
    string Application = "Anode");
