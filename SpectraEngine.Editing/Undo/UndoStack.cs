using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Commands;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editing.Undo;

/// <summary>
/// The editor's bounded, linear undo/redo history over one <see cref="Scene"/>.
/// A transaction collapses a whole gesture into one entry; a new command drops
/// the redoable entries; a full history evicts its oldest entry.
/// </summary>
// Render thread only. Changed handlers must not push, undo, redo or open a
// transaction from inside the event.
public sealed class UndoStack
{
    /// <summary>The default history bound, in entries.</summary>
    public const int DefaultCapacity = 256;

    // Ring buffer. _head is the oldest entry's slot. [0.._cursor) is undoable,
    // [_cursor.._count) is redoable.
    private readonly IEditorCommand?[] _entries;
    private int _head;
    private int _count;
    private int _cursor;

    private readonly List<IEditorCommand> _transaction = [];
    private string? _transactionName;

    /// <summary>Creates an empty history for <paramref name="scene"/>.</summary>
    /// <param name="capacity">Maximum retained entries; must be at least 1.</param>
    public UndoStack(Scene scene, int capacity = DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        Scene = scene;
        _entries = new IEditorCommand?[capacity];
    }

    /// <summary>The scene this history edits.</summary>
    public Scene Scene { get; }

    /// <summary>The maximum number of retained entries.</summary>
    public int Capacity => _entries.Length;

    /// <summary>Retained entries, undoable and redoable together.</summary>
    public int Count => _count;

    /// <summary>How many entries are currently undoable.</summary>
    public int UndoCount => _cursor;

    /// <summary>How many entries are currently redoable.</summary>
    public int RedoCount => _count - _cursor;

    /// <summary>
    /// True when <see cref="Undo"/> would do something. False while a
    /// transaction is open.
    /// </summary>
    public bool CanUndo => !IsTransactionOpen && _cursor > 0;

    /// <summary>
    /// True when <see cref="Redo"/> would do something. False while a
    /// transaction is open.
    /// </summary>
    public bool CanRedo => !IsTransactionOpen && _cursor < _count;

    /// <summary>True while a transaction is open.</summary>
    public bool IsTransactionOpen => _transactionName is not null;

    /// <summary>The open transaction's label, or null when none is open.</summary>
    public string? OpenTransactionName => _transactionName;

    /// <summary>
    /// The name of the entry <see cref="Undo"/> would revert, or null when
    /// <see cref="CanUndo"/> is false.
    /// </summary>
    public string? UndoName => CanUndo ? EntryAt(_cursor - 1).Name : null;

    /// <summary>
    /// The name of the entry <see cref="Redo"/> would re-apply, or null when
    /// <see cref="CanRedo"/> is false.
    /// </summary>
    public string? RedoName => CanRedo ? EntryAt(_cursor).Name : null;

    /// <summary>
    /// Raised after a recorded entry, an undo, a redo, a clear and each
    /// transaction boundary. Recording into an open transaction raises nothing.
    /// </summary>
    public event Action? Changed;

    /// <summary>Applies <paramref name="command"/> to the scene and records it.</summary>
    public void Execute(IEditorCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        command.Do(Scene);
        Record(command);
    }

    /// <summary>
    /// Records a command whose effect is already applied to the scene. The
    /// command is not executed.
    /// </summary>
    public void Record(IEditorCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (IsTransactionOpen)
        {
            // Coalescing keeps this list at one entry per target, not per frame.
            for (int i = _transaction.Count - 1; i >= 0; i--)
            {
                if (_transaction[i] is ICoalescingCommand coalescing && coalescing.TryAbsorb(command))
                    return;
            }

            _transaction.Add(command);
            return;
        }

        PushEntry(command);
        Changed?.Invoke();
    }

    /// <summary>
    /// Opens a transaction: every command recorded until
    /// <see cref="CommitTransaction"/> collapses into one history entry.
    /// Transactions do not nest; opening a second one throws.
    /// </summary>
    public void BeginTransaction(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (IsTransactionOpen)
        {
            throw new InvalidOperationException(
                $"An edit transaction ('{_transactionName}') is already open; transactions do not nest.");
        }

        _transactionName = name;
        // CanUndo/CanRedo just went false.
        Changed?.Invoke();
    }

    /// <summary>
    /// Closes the open transaction and lands its commands as one history
    /// entry, or none when it recorded nothing. Throws when no transaction is
    /// open.
    /// </summary>
    public void CommitTransaction()
    {
        RequireOpenTransaction();

        string name = _transactionName!;
        _transactionName = null;

        if (_transaction.Count == 1)
        {
            // No wrapper, so the command keeps its own name in the undo menu.
            PushEntry(_transaction[0]);
        }
        else if (_transaction.Count > 1)
        {
            PushEntry(new CompositeCommand(name, _transaction));
        }

        _transaction.Clear();
        // Raised even for an empty transaction: CanUndo/CanRedo are live again.
        Changed?.Invoke();
    }

    /// <summary>
    /// Closes the open transaction, rolls the scene back to its pre-gesture
    /// state and discards the commands. Throws when no transaction is open.
    /// </summary>
    public void CancelTransaction()
    {
        RequireOpenTransaction();

        // Reverse order: the commands may depend on each other.
        // RollBack, not Undo: Undo skips a node that left the scene, and these
        // commands are discarded, so nothing else would restore it.
        for (int i = _transaction.Count - 1; i >= 0; i--)
            _transaction[i].RollBack(Scene);

        _transaction.Clear();
        _transactionName = null;
        Changed?.Invoke();
    }

    /// <summary>
    /// Reverts the most recent undoable entry. False when
    /// <see cref="CanUndo"/> is false.
    /// </summary>
    public bool Undo()
    {
        if (!CanUndo)
            return false;

        _cursor--;
        EntryAt(_cursor).Undo(Scene);
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Re-applies the most recently undone entry. False when
    /// <see cref="CanRedo"/> is false.
    /// </summary>
    public bool Redo()
    {
        if (!CanRedo)
            return false;

        EntryAt(_cursor).Do(Scene);
        _cursor++;
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Drops the whole history without touching the scene. Throws when a
    /// transaction is open.
    /// </summary>
    public void Clear()
    {
        if (IsTransactionOpen)
        {
            throw new InvalidOperationException(
                $"Cannot clear the undo history while the transaction '{_transactionName}' is open.");
        }

        if (_count == 0)
            return;

        // Null the slots so dropped commands can be collected.
        for (int i = 0; i < _count; i++)
            _entries[RingIndex(i)] = null;

        _head = 0;
        _count = 0;
        _cursor = 0;
        Changed?.Invoke();
    }

    private void PushEntry(IEditorCommand command)
    {
        // Drop the redoable tail.
        for (int i = _cursor; i < _count; i++)
            _entries[RingIndex(i)] = null;
        _count = _cursor;

        if (_count == Capacity)
        {
            // Full: evict the oldest.
            _entries[_head] = null;
            _head = _head + 1 == Capacity ? 0 : _head + 1;
            _count--;
            _cursor--;
        }

        _entries[RingIndex(_count)] = command;
        _count++;
        _cursor = _count;
    }

    // _head + i < 2 * Capacity, so a subtract replaces the modulo.
    private int RingIndex(int logicalIndex)
    {
        int index = _head + logicalIndex;
        return index >= Capacity ? index - Capacity : index;
    }

    // Slots inside [0.._count) always hold a command.
    private IEditorCommand EntryAt(int logicalIndex) => _entries[RingIndex(logicalIndex)]!;

    private void RequireOpenTransaction()
    {
        if (!IsTransactionOpen)
            throw new InvalidOperationException("No edit transaction is open.");
    }
}
