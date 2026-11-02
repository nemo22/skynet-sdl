namespace SkyNet.Game.Campaign;

/// <summary>How a mission starts: the next one, or the same one again.</summary>
public enum MissionStart { Next, Restart }

/// <summary>
/// The won mission's movie (row +0x43), held from the WELL DONE banner until the next mission starts.
/// <code>
///   Won(SEQ30A) ─► Take(Next): SEQ30A, before the briefing
///              └─► Drop() (title, new game, load) or Take(Restart): gone
/// </code>
/// </summary>
public sealed class WinMovieSlot
{
    private string? _movie;

    public void Won(string? movie) => _movie = movie;

    /// <summary>The game was left: the movie belongs to no mission now.</summary>
    public void Drop() => _movie = null;

    /// <summary>The movie to play before a mission starts (none on a restart); empties the slot.</summary>
    public string? Take(MissionStart start)
    {
        var movie = _movie;
        _movie = null;
        return start == MissionStart.Next ? movie : null;
    }
}
