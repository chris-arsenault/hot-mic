# HotMic Requirements Progress

Status legend:
- Implemented: Code path exists and wired for typical use.
- Partial: Some pieces exist but missing UI, validation, or full behavior.
- Not Started: No implementation yet.
- Needs Validation: Implemented in code, but not verified against real devices/targets.

## Functional Requirements

### FR-1: Audio Input
| ID | Status | Notes |
| --- | --- | --- |
| FR-1.1 | Implemented | Dual WASAPI capture wired for two inputs. |
| FR-1.2 | Implemented | WASAPI capture via NAudio. |
| FR-1.3 | Implemented | Device selection UI + DeviceManager. |
| FR-1.4 | Implemented | UI selection persisted; takes effect on restart. |
| FR-1.5 | Implemented | UI selection persisted; takes effect on restart. |

### FR-2: Audio Output
| ID | Status | Notes |
| --- | --- | --- |
| FR-2.1 | Implemented | Output routed to selected VB-Cable device. |
| FR-2.2 | Implemented | VB-Cable detection on startup. |
| FR-2.3 | Implemented | Status message when missing. |
| FR-2.4 | Implemented | Optional monitor output device supported. |

### FR-3: Channel Strip
| ID | Status | Notes |
| --- | --- | --- |
| FR-3.1 | Implemented | Two independent ChannelStrips. |
| FR-3.2 | Implemented | Input gain control wired. |
| FR-3.3 | Implemented | Output gain control wired. |
| FR-3.4 | Implemented | Pre-plugin meter. |
| FR-3.5 | Implemented | Post-plugin meter. |
| FR-3.6 | Implemented | Mute toggle wired. |
| FR-3.7 | Implemented | Solo toggle wired. |

### FR-4: Plugin Chain
| ID | Status | Notes |
| --- | --- | --- |
| FR-4.1 | Implemented | Fixed 5-slot chain per channel. |
| FR-4.2 | Implemented | Ordered processing. |
| FR-4.3 | Implemented | Drag-and-drop reorder in UI. |
| FR-4.4 | Implemented | Bypass toggle wired to audio thread. |
| FR-4.5 | Implemented | Remove button clears slot. |
| FR-4.6 | Implemented | Empty slot shows Add Plugin action. |

### FR-5: Built-in Plugins
| ID | Status | Notes |
| --- | --- | --- |
| FR-5.1 | Implemented | Compressor plugin exists. |
| FR-5.1.1 | Implemented | Threshold range supported. |
| FR-5.1.2 | Implemented | Ratio range supported. |
| FR-5.1.3 | Implemented | Attack range supported. |
| FR-5.1.4 | Implemented | Release range supported. |
| FR-5.1.5 | Implemented | Makeup gain supported. |
| FR-5.1.6 | Implemented | Gain reduction meter shown in compressor parameters. |
| FR-5.2 | Implemented | Noise gate plugin exists. |
| FR-5.2.1 | Implemented | Threshold range supported. |
| FR-5.2.2 | Implemented | Attack range supported. |
| FR-5.2.3 | Implemented | Hold range supported. |
| FR-5.2.4 | Implemented | Release range supported. |
| FR-5.2.5 | Implemented | Gate open indicator shown in noise gate parameters. |
| FR-5.3 | Implemented | FFT noise removal plugin exists. |
| FR-5.3.1 | Implemented | Learn noise profile action available in parameters. |
| FR-5.3.2 | Implemented | Reduction parameter supported. |
| FR-5.3.3 | Implemented | Sensitivity parameter supported. |
| FR-5.4 | Implemented | 3-band EQ plugin exists. |
| FR-5.4.1 | Implemented | Low band gain/freq supported. |
| FR-5.4.2 | Implemented | Mid band gain/freq supported. |
| FR-5.4.3 | Implemented | High band gain/freq supported. |
| FR-5.4.4 | Implemented | Q/bandwidth per band supported. |

### FR-6: VST3 Plugin Support
| ID | Status | Notes |
| --- | --- | --- |
| FR-6.1 | Partial | VST.NET host wrapper added; not validated with VST3 binaries. |
| FR-6.2 | Implemented | Scans common VST3 directories plus VST2 plugin paths. |
| FR-6.3 | Implemented | Scan results cached to JSON; cache refreshed for VST2/VST3. |
| FR-6.4 | Implemented | VST3 editor window hosts native plugin UI. |
| FR-6.5 | Partial | Chunk/parameter state supported and persisted. |

### FR-7: Metering
| ID | Status | Notes |
| --- | --- | --- |
| FR-7.1 | Implemented | Peak and RMS rendered. |
| FR-7.2 | Implemented | Peak shown as line. |
| FR-7.3 | Implemented | RMS shown as filled bar. |
| FR-7.4 | Implemented | Peak hold + decay in MeterProcessor. |
| FR-7.5 | Implemented | Meter update timer ~30fps; UI redraw targets ~60fps. |
| FR-7.6 | Implemented | Tick marks + dB labels. |
| FR-7.7 | Implemented | Red indicator at clipping. |

### FR-8: User Interface - General
| ID | Status | Notes |
| --- | --- | --- |
| FR-8.1 | Implemented | Skia-rendered dark Ableton-style layout. |
| FR-8.2 | Implemented | Always-on-top toggle wired. |
| FR-8.3 | Implemented | Dark theme resources. |
| FR-8.4 | Implemented | Core UI rendered via SkiaSharp; VST3 editors hosted natively. |
| FR-8.5 | Partial | Skia-only rendering; responsiveness not profiled. |

### FR-9: User Interface - View Modes
| ID | Status | Notes |
| --- | --- | --- |
| FR-9.1 | Implemented | Full edit view. |
| FR-9.2 | Implemented | Channel strip controls in full view. |
| FR-9.3 | Implemented | Drag/drop in full view. |
| FR-9.4 | Implemented | Skia parameter editor for plugin controls. |
| FR-9.5 | Implemented | Minimal view. |
| FR-9.6 | Implemented | Minimal view shows names + meters. |
| FR-9.7 | Implemented | Minimal size enforced on toggle. |
| FR-9.8 | Implemented | Toggle between views. |

### FR-10: Configuration & Persistence
| ID | Status | Notes |
| --- | --- | --- |
| FR-10.1 | Partial | Config persists current settings; some settings lack UI. |
| FR-10.2 | Implemented | Plugin chain persisted. |
| FR-10.3 | Implemented | Plugin parameters persisted. |
| FR-10.4 | Implemented | Window position and view mode persisted. |
| FR-10.5 | Implemented | Device selections persisted. |
| FR-10.6 | Implemented | JSON config via System.Text.Json. |
| FR-10.7 | Not Started | Presets/profiles not implemented. |

## Non-Functional Requirements

### NFR-1: Performance
| ID | Status | Notes |
| --- | --- | --- |
| NFR-1.1 | Needs Validation | No latency measurements performed. |
| NFR-1.2 | Needs Validation | CPU usage not profiled. |
| NFR-1.3 | Needs Validation | CPU usage not profiled. |
| NFR-1.4 | Needs Validation | UI frame rate not measured. |
| NFR-1.5 | Needs Validation | Meter update rate set; not measured. |
| NFR-1.6 | Needs Validation | Memory usage not measured. |
| NFR-1.7 | Needs Validation | No audio glitch testing performed. |

### NFR-2: Compatibility
| ID | Status | Notes |
| --- | --- | --- |
| NFR-2.1 | Needs Validation | Targets Windows; not tested on 10/11. |
| NFR-2.2 | Implemented | net8.0-windows target. |
| NFR-2.3 | Implemented | Platform target set to x64 in build props. |
| NFR-2.4 | Implemented | VB-Cable required and validated. |

### NFR-3: Reliability
| ID | Status | Notes |
| --- | --- | --- |
| NFR-3.1 | Partial | Recovery loop implemented; not validated with real devices. |
| NFR-3.2 | Partial | VST wrapper catches exceptions; no UI notification. |
| NFR-3.3 | Implemented | Config load failure falls back to defaults. |
