namespace PileDesign.Common.Undo;

public interface IUndoAction
{
    void Undo();
    void Redo();
    string? Description { get; }
}