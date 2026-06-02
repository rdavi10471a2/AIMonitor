# Safe Edit Workflow

The AIMonitor workflow keeps the core safety invariant:

```text
inspect -> stage -> validate -> stable diff -> accept/reject hash classification -> record -> iterate
```

WinMerge review is the human decision gate. AIMonitor makes that gate safer and more manageable by surrounding it with:

- small edit surfaces;
- compiler/build/runtime feedback;
- regression tests;
- diff shape checks;
- durable state;
- findings that become tests when feasible.
