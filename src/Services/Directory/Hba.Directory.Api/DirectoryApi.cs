namespace Hba.Directory.Api;

/// <summary>
/// Point d'entrée nommé de cet hôte, pour WebApplicationFactory.
///
/// La classe Program générée par les instructions de haut niveau vit dans le
/// namespace global : deux hôtes référencés par un même projet de test la
/// rendent ambiguë. Ce marqueur désigne l'assembly sans ambiguïté.
/// </summary>
public sealed class DirectoryApi;
