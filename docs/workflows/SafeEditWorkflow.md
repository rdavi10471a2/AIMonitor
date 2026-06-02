# Safe Edit Workflow

The AIMonitor workflow keeps the core safety invariant:

```text
inspect -> stage -> validate -> stable diff -> accept/reject hash classification -> record -> iterate
```

Line-by-line human review is not the only safety mechanism. The workflow also relies on:

- small edit surfaces;
- compiler/build/runtime feedback;
- regression tests;
- diff shape checks;
- durable state;
- findings that become tests when feasible.
