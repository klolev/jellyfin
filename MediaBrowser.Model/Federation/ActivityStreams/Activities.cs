#pragma warning disable SA1402
#pragma warning disable SA1649

namespace MediaBrowser.Model.Federation.ActivityStreams;

/// <summary>
/// Accept activity.
/// </summary>
public class Accept : Activity
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Accept"/> class.
    /// </summary>
    public Accept()
    {
        Type = "Accept";
    }
}

/// <summary>
/// TentativeAccept activity.
/// </summary>
public class TentativeAccept : Accept
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TentativeAccept"/> class.
    /// </summary>
    public TentativeAccept()
    {
        Type = "TentativeAccept";
    }
}

/// <summary>
/// Add activity.
/// </summary>
public class Add : Activity
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Add"/> class.
    /// </summary>
    public Add()
    {
        Type = "Add";
    }
}

/// <summary>
/// Create activity.
/// </summary>
public class Create : Activity
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Create"/> class.
    /// </summary>
    public Create()
    {
        Type = "Create";
    }
}

/// <summary>
/// Delete activity.
/// </summary>
public class Delete : Activity
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Delete"/> class.
    /// </summary>
    public Delete()
    {
        Type = "Delete";
    }
}

/// <summary>
/// Follow activity.
/// </summary>
public class Follow : Activity
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Follow"/> class.
    /// </summary>
    public Follow()
    {
        Type = "Follow";
    }
}

/// <summary>
/// Ignore activity.
/// </summary>
public class Ignore : Activity
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Ignore"/> class.
    /// </summary>
    public Ignore()
    {
        Type = "Ignore";
    }
}

/// <summary>
/// Join activity.
/// </summary>
public class Join : Activity
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Join"/> class.
    /// </summary>
    public Join()
    {
        Type = "Join";
    }
}

/// <summary>
/// Leave activity.
/// </summary>
public class Leave : Activity
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Leave"/> class.
    /// </summary>
    public Leave()
    {
        Type = "Leave";
    }
}

/// <summary>
/// Like activity.
/// </summary>
public class Like : Activity
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Like"/> class.
    /// </summary>
    public Like()
    {
        Type = "Like";
    }
}

/// <summary>
/// Offer activity.
/// </summary>
public class Offer : Activity
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Offer"/> class.
    /// </summary>
    public Offer()
    {
        Type = "Offer";
    }
}

/// <summary>
/// Invite activity.
/// </summary>
public class Invite : Offer
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Invite"/> class.
    /// </summary>
    public Invite()
    {
        Type = "Invite";
    }
}

/// <summary>
/// Reject activity.
/// </summary>
public class Reject : Activity
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Reject"/> class.
    /// </summary>
    public Reject()
    {
        Type = "Reject";
    }
}

/// <summary>
/// TentativeReject activity.
/// </summary>
public class TentativeReject : Reject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TentativeReject"/> class.
    /// </summary>
    public TentativeReject()
    {
        Type = "TentativeReject";
    }
}

/// <summary>
/// Remove activity.
/// </summary>
public class Remove : Activity
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Remove"/> class.
    /// </summary>
    public Remove()
    {
        Type = "Remove";
    }
}

/// <summary>
/// Undo activity.
/// </summary>
public class Undo : Activity
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Undo"/> class.
    /// </summary>
    public Undo()
    {
        Type = "Undo";
    }
}

/// <summary>
/// Update activity.
/// </summary>
public class Update : Activity
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Update"/> class.
    /// </summary>
    public Update()
    {
        Type = "Update";
    }
}

/// <summary>
/// View activity.
/// </summary>
public class View : Activity
{
    /// <summary>
    /// Initializes a new instance of the <see cref="View"/> class.
    /// </summary>
    public View()
    {
        Type = "View";
    }
}

/// <summary>
/// Listen activity.
/// </summary>
public class Listen : Activity
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Listen"/> class.
    /// </summary>
    public Listen()
    {
        Type = "Listen";
    }
}

/// <summary>
/// Read activity.
/// </summary>
public class Read : Activity
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Read"/> class.
    /// </summary>
    public Read()
    {
        Type = "Read";
    }
}

/// <summary>
/// Move activity.
/// </summary>
public class Move : Activity
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Move"/> class.
    /// </summary>
    public Move()
    {
        Type = "Move";
    }
}

/// <summary>
/// Announce activity.
/// </summary>
public class Announce : Activity
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Announce"/> class.
    /// </summary>
    public Announce()
    {
        Type = "Announce";
    }
}

/// <summary>
/// Block activity.
/// </summary>
public class Block : Ignore
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Block"/> class.
    /// </summary>
    public Block()
    {
        Type = "Block";
    }
}

/// <summary>
/// Flag activity.
/// </summary>
public class Flag : Activity
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Flag"/> class.
    /// </summary>
    public Flag()
    {
        Type = "Flag";
    }
}

/// <summary>
/// Dislike activity.
/// </summary>
public class Dislike : Activity
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Dislike"/> class.
    /// </summary>
    public Dislike()
    {
        Type = "Dislike";
    }
}
