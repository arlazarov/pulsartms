# Dispatch rendering and truck labels release

The user explicitly requested deployment without checks, plus an operational
inspection of routes for trucks 54777 and 11005.

Firebase Hosting successfully released the fresh Client artifact from
`artifacts/managed/release-OoCy7c/publish/wwwroot` to project `amftms`.
The publish included distinct Dispatch section keys and nearby-truck label
placement. Only the required publish/build ran; no automated tests, release
gate, post-deployment smoke or asset verification ran in this deployment turn.
The API revision and database schema were not changed.

The explicitly requested read-only route inspection found:

- AMF1377 header truck: 11005; final stops assigned to 54777.
- AMF1383 header truck: 54777; final stops assigned to 11005.
- Each saved legacy route still belongs to its header truck and contains only
  the final stop. Neither provides proof of the new truck's complete itinerary.
- No native execution legs exist. Exchange visits remain partially completed
  in imported data; missing Drop/Hook facts were not invented or recorded.

Full correct live routes were not established. Operator confirmation of the
actual exchange is still needed before changing operational history. The
authenticated browser could not be inspected because the browser tool's Node
runtime failed to start. No route, assignment or completion rows were modified.

## Centered group label follow-up

After the operator reported that the `2 trucks` label appeared progressively
farther away, group-label collision displacement was removed. The group label
now sits directly at its geographic center with a zero pixel offset. Individual
truck labels retain their separate collision layout. Station and stop changes
no longer trigger group-label placement. The exact reported runtime movement
was not reproduced; this change removes the intentional offset entirely.

The user again requested publication without tests. Firebase successfully
released `artifacts/managed/release-omyprb/publish/wwwroot`. Only formatting,
the necessary Client publish/build and Hosting deployment ran. Updated
regression sources were not executed; no browser or post-deployment checks ran.
The outstanding exchange/route confirmation above remains unresolved, and no
operational database records or API revision were changed.
