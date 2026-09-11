# Road camera snapshots

Fleet Map offers Follow, Calculate Fuel and Camera as labelled icon buttons.
Opening Camera and pressing Refresh both request a new road-facing still image. It is not
live video; the displayed timestamp comes from the returned media start time.

Authenticated API endpoints dispatch through MediatR. Application resolves the
active truck and owns a ten-minute retrieval mapping. Infrastructure implements
ITruckCameraProvider using the Samsara media retrieval API. Browser callers never
supply Samsara vehicle or retrieval identifiers. Signed HTTPS image URLs are
returned only through the authenticated no-store endpoint and are not logged.

Refresh requests a new capture and is disabled while awaiting its result.
Polling also checks the latest uploaded media, and completes only when an image
newer than the displayed capture and captured no earlier than 15 seconds before
the click is found. This tolerance accommodates the server's five-second lookback
and timestamp rounding. Older fallback images do not complete the request, even
when opening the camera for the first time. An available but unchanged retrieval
does not count as a successful refresh. Completion explicitly renders the dialog.
It is enabled again after completion, error or timeout. Samsara quotas and rate limits still apply. The browser
checks its status every five seconds for up to five minutes, and stops when the
dialog closes or the selected truck changes. No automatic new capture is issued.
While pending, the last uploaded road image from the previous hour may be shown
with its actual timestamp, without extra waiting text. This fallback lookup
is cached for 30 seconds.
The mapping is process-local, matching the current single-instance
deployment; multi-instance deployment requires shared coordination.

Samsara requires Read Media Retrieval and Write Media Retrieval token scopes.
Camera uploads consume the organization's media retrieval quota. Offline cameras
may upload later; unavailable recordings and permission errors are shown in the
dialog. The existing image
remains visible with its original timestamp while waiting.

Reference: https://developers.samsara.com/reference/postmediaretrieval
and https://developers.samsara.com/reference/getmediaretrieval.
