"""Configuration system for APS algorithm parameters.

Loads algorithm configuration from Algorithm_Config sheet in workbook,
validates all parameters, and provides central access point for all
hardcoded business rules.
"""
import pandas as pd
from typing import Any, Dict, Optional, Union, List
from datetime import datetime
import numpy as np


class AlgorithmConfig:
    """Manages algorithm configuration parameters from Excel.

    All hardcoded business rules are loaded from Algorithm_Config sheet
    on scheduler startup. Provides type-safe access with validation.
    """

    def __init__(self, config_df: pd.DataFrame = None):
        """Initialize config from Algorithm_Config sheet.

        Args:
            config_df: DataFrame from Algorithm_Config sheet, or None to use defaults
        """
        self.config_dict = {}
        self.metadata = {}

        if config_df is not None and not config_df.empty:
            self._load_from_dataframe(config_df)

    def _load_from_dataframe(self, config_df: pd.DataFrame) -> None:
        """Parse Algorithm_Config sheet and populate internal dict.

        Expected columns:
            A: Config_Key (required)
            D: Current_Value (required)
            E: Data_Type (required)
            F: Min_Value (optional)
            G: Max_Value (optional)
        """
        for _, row in config_df.iterrows():
            key = str(row.get('Config_Key', '')).strip()
            if not key:
                continue

            value = row.get('Current_Value')
            data_type = str(row.get('Data_Type', '')).strip().upper()
            min_val = row.get('Min_Value')
            max_val = row.get('Max_Value')

            # Convert value to appropriate type
            converted = self._convert_value(value, data_type)

            # Validate
            if not self._validate_value(key, converted, data_type, min_val, max_val):
                raise ValueError(
                    f"Invalid config value for {key}: {converted} "
                    f"(type={data_type}, min={min_val}, max={max_val})"
                )

            self.config_dict[key] = converted
            self.metadata[key] = {
                'data_type': data_type,
                'min': min_val,
                'max': max_val,
                'category': str(row.get('Category', '')).strip(),
                'description': str(row.get('Description', '')).strip(),
            }

    def _convert_value(self, value: Any, data_type: str) -> Any:
        """Convert raw value to proper Python type."""
        if pd.isna(value) or value is None:
            return None

        data_type = str(data_type).upper()

        if data_type == 'BOOLEAN':
            return str(value).strip().upper() in {'TRUE', 'YES', '1', 'Y'}
        elif data_type == 'DURATION':
            return int(float(value))
        elif data_type == 'COUNT':
            return int(float(value))
        elif data_type == 'QUANTITY':
            return float(value)
        elif data_type == 'PERCENTAGE':
            return float(value)
        elif data_type == 'WEIGHT':
            return float(value)
        elif data_type == 'RATIO':
            return float(value)
        elif data_type == 'THRESHOLD':
            return float(value)
        elif data_type in {'LIST', 'SET', 'CHOICE'}:
            if isinstance(value, str):
                return [v.strip() for v in value.split(',') if v.strip()]
            return list(value) if value else []
        else:
            return str(value).strip()

    def _validate_value(self, key: str, value: Any, data_type: str,
                       min_val: Any, max_val: Any) -> bool:
        """Validate value is within acceptable bounds."""
        if value is None:
            return True

        # Numeric validation
        if data_type in {'DURATION', 'COUNT', 'QUANTITY', 'PERCENTAGE', 'WEIGHT', 'RATIO', 'THRESHOLD'}:
            try:
                num_val = float(value)
                if pd.notna(min_val) and num_val < float(min_val):
                    return False
                if pd.notna(max_val) and num_val > float(max_val):
                    return False
            except (ValueError, TypeError):
                return False

        return True

    def get(self, key: str, default: Any = None) -> Any:
        """Get config value by key.

        Args:
            key: Config parameter key
            default: Default value if key not found

        Returns:
            Configured value or default
        """
        return self.config_dict.get(key, default)

    def get_duration_minutes(self, key: str, default: int = 0) -> int:
        """Get duration parameter in minutes."""
        val = self.get(key, default)
        return int(val) if val is not None else default

    def get_percentage(self, key: str, default: float = 0.0) -> float:
        """Get percentage parameter (0-100 or 0-1 format)."""
        val = self.get(key, default)
        return float(val) if val is not None else default

    def get_weight(self, key: str, default: int = 1) -> int:
        """Get weight parameter (priority weights, penalties)."""
        val = self.get(key, default)
        return int(val) if val is not None else default

    def get_float(self, key: str, default: float = 0.0) -> float:
        """Get float parameter."""
        val = self.get(key, default)
        return float(val) if val is not None else default

    def get_list(self, key: str, default: List[str] = None) -> List[str]:
        """Get list parameter."""
        default = default or []
        val = self.get(key, default)
        if isinstance(val, list):
            return val
        return default

    def get_bool(self, key: str, default: bool = False) -> bool:
        """Get boolean parameter."""
        val = self.get(key, default)
        return bool(val) if val is not None else default

    def all_params(self) -> Dict[str, Any]:
        """Get all parameters as dict."""
        return dict(self.config_dict)

    def params_by_category(self, category: str) -> Dict[str, Any]:
        """Get all parameters in a category."""
        return {
            k: v for k, v in self.config_dict.items()
            if self.metadata.get(k, {}).get('category') == category
        }

    def update(self, key: str, value: Any, user: str = 'SYSTEM', reason: str = '') -> bool:
        """Update a config value (would write back to Excel).

        Args:
            key: Config parameter key
            value: New value
            user: User making change
            reason: Reason for change

        Returns:
            True if successful
        """
        # Validate
        meta = self.metadata.get(key, {})
        if not self._validate_value(key, value, meta.get('data_type'),
                                   meta.get('min'), meta.get('max')):
            return False

        # Update
        self.config_dict[key] = value

        # In real implementation, would write to Excel here
        # For now, just update in memory
        return True


# Global singleton instance
_config_instance: Optional[AlgorithmConfig] = None


def load_algorithm_config(config_df: pd.DataFrame) -> AlgorithmConfig:
    """Load algorithm config from dataframe and set as global singleton."""
    global _config_instance
    _config_instance = AlgorithmConfig(config_df)
    return _config_instance


def get_config() -> AlgorithmConfig:
    """Get global config instance."""
    global _config_instance
    if _config_instance is None:
        _config_instance = AlgorithmConfig()
    return _config_instance
