"""Tests for queue-time handling — NaN safety and constraint enforcement."""
import sys
from pathlib import Path

import pandas as pd
import pytest

sys.path.insert(0, str(Path(__file__).parent.parent))

from engine.scheduler import _normalize_queue_times


class TestQueueTimeNormalization:
    def test_normal_values(self):
        raw = {
            ("EAF", "LRF"): {"min": 5, "max": 30, "enforcement": "Hard"},
        }
        result = _normalize_queue_times(raw)
        assert result[("EAF", "LRF")]["min"] == 5
        assert result[("EAF", "LRF")]["max"] == 30

    def test_nan_min_defaults_to_zero(self):
        raw = {
            ("EAF", "LRF"): {"min": float("nan"), "max": 30, "enforcement": "Hard"},
        }
        result = _normalize_queue_times(raw)
        assert result[("EAF", "LRF")]["min"] == 0

    def test_nan_max_defaults_to_9999(self):
        raw = {
            ("EAF", "LRF"): {"min": 5, "max": float("nan"), "enforcement": "Hard"},
        }
        result = _normalize_queue_times(raw)
        assert result[("EAF", "LRF")]["max"] == 9999

    def test_none_input_returns_empty(self):
        result = _normalize_queue_times(None)
        assert result == {}

    def test_empty_dict_returns_empty(self):
        result = _normalize_queue_times({})
        assert result == {}

    def test_operation_aliases_are_resolved(self):
        raw = {
            ("MELTING", "REFINING"): {"min": 5, "max": 30, "enforcement": "Soft"},
        }
        result = _normalize_queue_times(raw)
        assert ("EAF", "LRF") in result
        assert result[("EAF", "LRF")]["enforcement"] == "SOFT"
