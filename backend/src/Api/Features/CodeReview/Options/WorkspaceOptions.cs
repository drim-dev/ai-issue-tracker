using System.ComponentModel.DataAnnotations;

namespace Api.Features.CodeReview.Options;

/// <summary>
/// Filesystem layout the reviewer reads design/plan/spec docs from. The CLI defaults to
/// the repo's <c>docs/</c> directory; tests override it to point at a fixture folder.
/// </summary>
public sealed class WorkspaceOptions
{
    public const string SectionName = "Workspace";

    [Required(AllowEmptyStrings = false)]
    public string DocsRoot { get; init; } = "docs";
}
