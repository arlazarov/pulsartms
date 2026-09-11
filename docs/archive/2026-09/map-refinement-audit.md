# Map refinement audit

Scope: recent next-load connections, route appearance, and truck-panel close control.

- Application validates saved deadhead signatures and predecessor identity. Map reads do not call the routing provider. Deadheads are read in batches rather than once per future load.
- The map connection DTO sends mileage and one geometry, not both the full path and repeated leg geometry. Client and Application maintain independent contracts.
- Next-load lines and marker grouping share local helpers. Identical refreshes retain existing objects; hiding or disposing the layer releases them.
- Route appearance belongs to one rendering module. Outlines reuse path data and cached layers. They add one GPU layer per route line; runtime GPU performance has not been measured.
- The close control uses existing control mixins and named size/spacing tokens. No page-specific color literals were introduced.

Next-load polling sends the last successfully rendered revision. An identical response omits routes and skips client serialization and map updates. Selection changes and layer clearing reset the revision. The fingerprint covers the returned geometry, order, labels and deadhead mileage.

Remaining limitations: the server still reads and fingerprints route data on each next-load poll; deadhead validation reads truck history. This change reduces network transfer, not database work. Current-route polling is outside this change. This review is not a full-application performance benchmark or browser soak test.
