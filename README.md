# UnityPerfetto

**UnityPerfetto** is a lightweight, performant C# Unity to [Perfetto](https://perfetto.dev/) util library for your tracing needs.
* Grouped **Publishers**
* Visualize **Slices** (intervals of time) & **Counters** (instantaneous)
* Decoupled callback registration with provided `PerfettoTraceManager`
* Easily attach metadata to any event with `PerfettoDictionary`
* Multithreaded to avoid overhead from writing to file
<img src="assets/example.jpg">

## How it works

1. Create your own trace manager by inheriting from `PerfettoTraceManager` (this class manages the lifespan
   of `ProtoWriter` which is a singleton that handles serialization of your data)
2. Expose your data through callbacks (e.g. `UnityEvent` and `UnityAction`) and register them in a `override` of
   `PerfettoTraceManager`'s abstract `ExtendInit` method and unregister them in an `override` of `PerfettoTraceManager`'s abstract
   `ExtendEnd()` method.
3. Currently, **UnityPerfetto** is only designed to visualize a single stream of info per publisher, so for any data you would like visualized,
   create the respective publisher (with arguments `name`, `group`)according to the type of visualization you require:
    1. Slice (Visualizes an interval of time. Useful for identifying states)
    2. Counter (Visualizes an instantaneous value. Useful for identifying magnitudes of values)
4. After steps 1-3, you should have a class that looks like
```csharp
using UnityEngine.Events;
using UnityPerfetto;
using UnityPerfetto.Protos;
public class ProtoBasketballBenchmarkManager : PerfettoTraceManager
{
    private UnityAction<ShotManager.ShotState> _onShotStateChange;
    private UnityAction<float> _onShotCharging;
    private UnityAction<float, float> _onShotReleased;

    private SlicePublisher _shotStatePublisher;
    private CounterPublisher _shotAnglePublisher;
    private CounterPublisher _shotReleasedInfoPublisher;

    public ShotManager shotManager;

    protected override void ExtendInit()
    {
        _onShotStateChange = (ShotManager.ShotState data) =>
        {
            HandleShotStateChange(data); // I recommend passing your data into a helper method to 
                                         // prevent bloat (especially when packaging additional metadata)
        };

        _onShotCharging = (float shotAngle) =>
        {
            HandleShotCharging(shotAngle);
        };

        _onShotReleased = (float basketballYPos, float gravityScale) =>
        {
            HandleShotReleased(basketballYPos, gravityScale);
        };

        // Register publishers using the appropriate publisher type
        _shotStatePublisher = PerfettoPublisherFactory.Instance.CreateSlicePublisher("Shot State", "Player");
        _shotAnglePublisher = PerfettoPublisherFactory.Instance.CreateCounterPublisher("Shot Angle", "Player");
        _shotReleasedInfoPublisher = PerfettoPublisherFactory.Instance.CreateCounterPublisher(
                                            "Shot Release Info",
                                            "Basketball"
                                     );

        // Add listeners to the ShotManager events
        shotManager.onShotStateChange.AddListener(_onShotStateChange);
        shotManager.onShotCharging.AddListener(_onShotCharging);
        shotManager.onShotReleased.AddListener(_onShotReleased);
    }

    protected override void ExtendEnd()
    {
        shotManager.onShotStateChange.RemoveAllListeners();
        shotManager.onShotCharging.RemoveAllListeners();
    }
}
```
5. Only one value for any publisher can actually be visualized, so in the case of `_onShotReleased` as seen above, you can package
   additional metadata information in with the help of `PerfettoDictionary`. In this example, `basketballYPos` is visualized, and
   all the additional information populated in `dict` can be seen by clicking on any data point from this publisher in Perfetto's UI.
```csharp
private void HandleShotReleased(float basketballYPos, float gravityScale)
{
    // Log the basketball height and associated gravity scale, along with some other categorized verbose info
    PerfettoDictionary dict = new PerfettoDictionary();
    dict["grav_scale"] = gravityScale;
    dict["more info", "basketball_name"] = "Larry";
    dict["more info", "age"] = "10";
    _shotReleasedInfoPublisher.LogCounterEvent(_shotAnglePublisher.GetTimeStamp(), basketballYPos, dict);
}
```
6. Note that for `SlicePublisher`'s (which correspond to [Thread Scoped Slices](https://perfetto.dev/docs/reference/synthetic-track-event#thread-scoped-sync-slices)),
   any additional events published must nest properly. In other words, any events published after an event must end before the original
   event ends (No partial overlap of events).
```csharp
private void HandleShotStateChange(ShotManager.ShotState newState)
{
    // End previous duration event if this is not the first state logged
    if (_isFirstStateLogged)
    {
        _shotStatePublisher.LogEndEvent(_shotStatePublisher.GetTimeStamp());
    }
    else
    {
        _isFirstStateLogged = true;
    }

    // Start a new duration event for the current state
    _shotStatePublisher.LogStartEvent(newState.ToString(), "player_info", _shotStatePublisher.GetTimeStamp());

    prevShotState = newState;
}
```
7. Now, just add the new BenchmarkManager script you've created to any GameObject, enable it as you see fit, and when your game exits,
   open up the generated *.pb file in [Perfetto's UI](https://ui.perfetto.dev/#!/info) and profit!

## Example PerfettoTraceManager
```csharp
using System;
using UnityEngine.Events;
using UnityPerfetto;
using UnityPerfetto.Protos;

public class ProtoBasketballBenchmarkManager : PerfettoTraceManager
{
    private UnityAction<ShotManager.ShotState> _onShotStateChange;
    private UnityAction<float> _onShotCharging;
    private UnityAction<float, float> _onShotReleased;

    private SlicePublisher _shotStatePublisher;
    private CounterPublisher _shotAnglePublisher;
    private CounterPublisher _shotReleasedInfoPublisher;

    public ShotManager shotManager;

    protected override void ExtendInit()
    {
        _onShotStateChange = (ShotManager.ShotState data) =>
        {
            HandleShotStateChange(data);
        };

        _onShotCharging = (float shotAngle) =>
        {
            HandleShotCharging(shotAngle);
        };

        _onShotReleased = (float basketballYPos, float gravityScale) =>
        {
            HandleShotReleased(basketballYPos, gravityScale);
        };

        // Register publishers using the appropriate publisher type
        _shotStatePublisher = PerfettoPublisherFactory.Instance.CreateSlicePublisher("Shot State", "Player");
        _shotAnglePublisher = PerfettoPublisherFactory.Instance.CreateCounterPublisher("Shot Angle", "Player");
        _shotReleasedInfoPublisher = PerfettoPublisherFactory.Instance.CreateCounterPublisher(
                                            "Shot Release Info",
                                            "Basketball"
                                     );

        // Add listeners to the ShotManager events
        shotManager.onShotStateChange.AddListener(_onShotStateChange);
        shotManager.onShotCharging.AddListener(_onShotCharging);
        shotManager.onShotReleased.AddListener(_onShotReleased);
    }

    protected override void ExtendEnd()
    {
        shotManager.onShotStateChange.RemoveAllListeners();
        shotManager.onShotCharging.RemoveAllListeners();
    }

    private bool _isFirstStateLogged = false;
    private ShotManager.ShotState prevShotState;

    private void HandleShotStateChange(ShotManager.ShotState newState)
    {
        // End previous duration event if this is not the first state logged
        if (_isFirstStateLogged)
        {
            _shotStatePublisher.LogEndEvent(_shotStatePublisher.GetTimeStamp());
        }
        else
        {
            _isFirstStateLogged = true;
        }

        // Start a new duration event for the current state
        _shotStatePublisher.LogStartEvent(newState.ToString(), "player_info", _shotStatePublisher.GetTimeStamp());

        prevShotState = newState;
    }

    private void HandleShotCharging(float shotAngle)
    {
        // Log the current shot angle as a counter event
        _shotAnglePublisher.LogCounterEvent(_shotAnglePublisher.GetTimeStamp(), shotAngle);
    }

    private void HandleShotReleased(float basketballYPos, float gravityScale)
    {
        // Log the basketball height and associated gravity scale, along with some other categorized verbose info
        PerfettoDictionary dict = new PerfettoDictionary();
        dict["grav_scale"] = gravityScale;
        dict["more info", "basketball_name"] = "Larry";
        dict["more info", "age"] = "10";
        _shotReleasedInfoPublisher.LogCounterEvent(_shotAnglePublisher.GetTimeStamp(), basketballYPos, dict);
    }
}
```
