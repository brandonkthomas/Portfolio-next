namespace Portfolio.Web.Models;

/// <summary>
/// Carries page-specific values so shared layouts can render navigation + document metadata
/// </summary>
/// <remarks>
/// Flow: _ViewStart => _PortfolioLayout => _BaseLayout
/// </remarks>
/// <param name="NavigationKey">Identifies the active portfolio navigation item.</param>
/// <param name="DocumentTitle">Provides the browser and social-sharing title.</param>
/// <param name="Description">Provides the document and social-sharing description.</param>
/// <param name="CanonicalPath">Provides the application-relative canonical path.</param>
/// <param name="OpenGraphType">Provides the Open Graph object type.</param>
public sealed record PageMetadata(
    string NavigationKey,
    string DocumentTitle,
    string Description,
    string CanonicalPath,
    string OpenGraphType = "website");
