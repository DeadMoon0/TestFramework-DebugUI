# StateService
A State Managing Service for WPF

## Graph
The Graph connects every Node (Object) together. Every Node can have Multiple Parents (like C# refs).

### Dispatched
The Graph lives on its own Thread/Task and every Action must be Dispatched. Every change is Applied in order (FIFO)

### Node-Store
The Graph has a Node Store where it Stores every Node. It is indexed by the GUID.
If a Node looses every Relation. It is removed from the Store.

### Callback-Map
A Map of all Callbacks.
WeakEventManager are used to not hinder the Target-Object from Disposing.

## Node (StateObjects)
The Object represents a Node in the Graph. A Node can hold "leaf"-Values (Values that do not are a State them selves)
Every Node has a unique GUID used to Identify it.

## Path
A Path Describes a way to a Value. This Value may or may not exist at this time.

### Callbacks
A Callback is a way to react to change.

| Flag | Description |
|---|---|
|DoNow| Simulate an Update with the current State (used to Init something) |
|NotNull| Does not trigger when the Value is now NULL |