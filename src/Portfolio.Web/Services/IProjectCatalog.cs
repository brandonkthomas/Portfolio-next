using Portfolio.Web.Content.Projects;

namespace Portfolio.Web.Services;

/// <summary>
/// Exposes validated project records w/o coupling pages to their JSON storage
/// </summary>
public interface IProjectCatalog
{
    /// <summary>Gets projects in display order.</summary>
    IReadOnlyList<ProjectRecord> Projects { get; }
}
