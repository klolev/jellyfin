namespace MediaBrowser.Model.Federation.ActivityStreams;

/// <summary>
/// ActivityStreams IntransitiveActivity type. Activities without a direct object.
/// </summary>
public class IntransitiveActivity : Object
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IntransitiveActivity"/> class.
    /// </summary>
    public IntransitiveActivity()
    {
        Type = "IntransitiveActivity";
    }
}
