# HOS display and ETA planning

## Test layout

Server tests live under `Server.Tests/{Dispatch,Eta,Fleet,Fuel,Identity,Routing,Synchronization}` in the `Server.Tests` project. Client C# tests live in the separate `Client.Tests` project. Use the shared categories in [testing](../testing.md); browser-map unit tests live under `Client/tests/fleetMap` and run through `npm test --prefix Client`.

## Status and continuous rest

Samsara `sleeperBed` is normalized to the application's `sleeperBerth` at the integration boundary. The existing all-driver HOS clock response supplies the current status. The history already retrieved for ETA supplies status start and continuous-rest start; displaying these fields makes no additional external API calls.

Consecutive logged Off Duty, Sleeper Berth and Personal Conveyance periods contribute to the continuous-rest total. Driving, On Duty and Yard Move interrupt it. Gaps, overlapping/conflicting records, stale snapshots and disagreement with current clock status prevent unverified rest credits. A recent verified suffix can provide the current rest even if older cycle history is incomplete.

The UI shows durations at the HOS snapshot time, not proof that a status has continued since the last reading. A 10-hour rest target is separate from cycle availability. PC is used as recorded by the ELD; the planner does not reclassify business driving as PC or certify that PC was used legally.

The same current-duty summary also shows the remaining continuous rest to a
cycle reset. Application selects the rule from the truck's fresh GPS region,
not the destination or home timezone: US property cycles use 34 hours; verified
Canadian Cycle 1 uses 36 and Cycle 2 uses 72. An unknown country, unsupported
ruleset, unknown Canadian cycle, stale history or unverified rest boundary does
not produce a reset countdown. The Client formats the server's remaining minutes
and target only. Reaching the displayed rest duration is not a write to ELD data
or an instruction to take a restart. Rules checked against
[FMCSA](https://www.fmcsa.dot.gov/regulations/hours-service/summary-hours-service-regulations)
and [Canadian section 28](https://laws-lois.justice.gc.ca/eng/regulations/SOR-2005-313/section-28.html).

For a US driver with at least three verified hours of ongoing rest, ETA assumes completion of a full 10-hour rest before departure. This is a conservative dispatch assumption, not a legal obligation to use ten hours when a valid split is available. Other supported split/rest and cycle constraints remain in the HOS clock. The underlying ELD is authoritative.

## Planning allowances

`EtaPlanning` follows the existing validated-options configuration pattern. Defaults:

| Setting | Default |
| --- | --- |
| DrivingHoursPerShift | 11 hours, further limited by available ELD clocks |
| PreTripMinutes | 15 minutes on duty before a new planned shift |
| FuelStopMinutes | One 5-minute on-duty fueling allowance per planned shift |
| DailyBreakMinutes | 30 minutes separately from fueling |
| PickupMinutes | 120 minutes planned Sleeper Berth |
| DeliveryMinutes | 120 minutes planned Sleeper Berth |
| PlanningSpeedCapMph | 60 mph planning cap, not a road speed limit |
| TravelTimeBufferPercent | 0%, no additional travel-time allowance |

PTI and fueling allowances are not added again to an already driven current shift;
its remaining ELD clocks are the starting point. Each new planned shift receives
15 minutes PTI and 5 minutes fueling once, regardless of the number of loads,
route segments or recommended fuel stations. These allowances and driving consume
Cycle; planned facility sleeper time does not. The configuration key
`FuelStopMinutes` is retained, but now controls the once-per-shift allowance.
The planner uses a separate break allowance and takes it no later than eight
planned driving hours when further driving is needed. It does not force an unused
break before final arrival. Short sleeper periods still consume the shift window;
they do not by themselves award a fresh daily shift.

Each leg uses the slower of its saved truck-routing travel time and the planning speed cap. Saved routing already requests traffic-aware travel times, so no additional percentage is applied by default. No extra routing request is triggered by ETA display. Slower road travel times are never accelerated to match the planning cap.

One driver clock continues across the current remaining route, its final delivery,
saved empty connections and every following assigned load. Pickups and deliveries
each add their configured service time before subsequent travel. An early arrival
waits until the appointment begins; lateness compares arrival with the appointment
window's end. Later appointment slack can absorb earlier delays. Stops already
reported complete do not receive another service allowance. When current GPS and
an actual arrival identify an unfinished current facility visit, only the remaining
service allowance is added.

For both current and future loads, remaining facility service and appointment
waiting are planned as Sleeper Berth. This is an approximate dispatch assumption,
not a claim about actual driver status, and it does not write duty records.
Consecutive waiting and service form one continuous planned rest, credited once:
qualifying daily rest preserves cycle consumption, with recap credited at verified
home-day boundaries. A future cycle restart is an explicit alternative, not an
assumed part of road ETA. US alternatives retain the 34-hour restart threshold;
Canada retains conservative daily spacing and configured restart thresholds.
Missing or stale recent history cannot produce a verified restart or recap
alternative. Missing older days alone do not hide projected consumption from a
known ELD Cycle: a fresh continuous suffix supports that anchor without adding
historical recap. Only an explicit fully observed qualifying restart can replace
the anchor; missing history is never treated as rest.
When a usable timeline's totals disagree with current ELD Cycle, the ELD balance
still anchors projected consumption. Historical recap remains unverified; an
explicit completed restart can establish a new full-cycle baseline.
A known different future driver or missing saved connection stops propagation;
already calculated earlier stops remain available.

Dispatch and map stops show one inline `Cycle remaining` arrival balance.
The post-service balance is not displayed, even when appointment waiting or recap
changes it. It remains in the server forecast for onward planning; hiding the row
does not change the calculation or the conditional `With recap` arrival.
Dispatch stops show compact road ETA, signed cycle balances and conditional
recap/restart alternatives; the detailed contract and shortage semantics are in
[ETA forecasting](eta-service.md#cycle-feasibility-and-conditional-alternatives).
The compact status block shows current status, its duration and
time remaining to the 10-hour rest target without a repeated continuous-rest total
or explanatory footer. PTI and fuel-stop totals remain in the server response for
verification. The result is a forecast, not an ELD compliance verdict or a
guaranteed arrival.

## Sources

- [Samsara HOS log payload including sleeperBed](https://developers.samsara.com/docs/insuretech)
- [Samsara current HOS clocks](https://developers.samsara.com/reference/gethosclocks)
- [FMCSA personal conveyance FAQ, including combination with 10/34-hour rest](https://www.fmcsa.dot.gov/regulations/hours-service/personal-conveyance-frequently-asked-questions-0)
- [FMCSA summary of property-carrier HOS rules](https://www.fmcsa.dot.gov/regulations/hours-service/summary-hours-service-regulations)
- [Canadian commercial-driver rules, sections 13–14 and 28](https://laws.justice.gc.ca/eng/regulations/SOR-2005-313/FullText.html)

The daily/cycle thresholds for forecast appointment waits were checked against
these primary sources on September 8, 2026. A forecast remains conditional on the
driver actually taking the assumed qualifying off-duty period.
