namespace Hba.Rating.Domain.Exceptions;

/// <summary>
/// Les refus de ce service, en toutes lettres.
/// </summary>
public static class RatingErrorCodes
{
    /// <summary>La note est hors du bareme : elle va de une a cinq etoiles.</summary>
    public const string InvalidStars = "INVALID_STARS";

    /// <summary>Cette mission a deja ete notee.</summary>
    public const string AlreadyReviewed = "ALREADY_REVIEWED";

    /// <summary>Le commentaire depasse ce qu'on accepte de garder.</summary>
    public const string CommentTooLong = "COMMENT_TOO_LONG";
}
