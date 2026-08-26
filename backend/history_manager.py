from __future__ import annotations
import os
import json
import uuid
import threading
from datetime import datetime
from typing import Optional, Dict, List, Any

class HistoryManager:
    """Manages persistent exam generation history and logs."""

    def __init__(self, config_dir=None):
        if config_dir is None:
            base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
            config_dir = os.path.join(base_dir, "data", "config")
        self.config_dir = config_dir
        os.makedirs(self.config_dir, exist_ok=True)
        
        self.history_file = os.path.join(self.config_dir, "exam_history.json")
        self._lock = threading.RLock()
        self.history = self._load_history()

    def _load_history(self) -> list:
        if not os.path.exists(self.history_file):
            return []
        try:
            with open(self.history_file, "r", encoding="utf-8") as f:
                data = json.load(f)
                return data if isinstance(data, list) else []
        except Exception as e:
            print(f"Error loading exam history: {e}, using empty list.")
            return []

    def _save_history(self):
        temp_path = self.history_file + ".tmp"
        try:
            with open(temp_path, "w", encoding="utf-8") as f:
                json.dump(self.history, f, ensure_ascii=False, indent=2)
            os.replace(temp_path, self.history_file)
        finally:
            if os.path.exists(temp_path):
                os.remove(temp_path)

    def add_batch(self, batch_data: dict) -> dict:
        """Adds a new exam generation batch record to history."""
        with self._lock:
            if not batch_data.get("batch_id"):
                batch_data["batch_id"] = f"batch_{datetime.now().strftime('%Y%m%d_%H%M%S')}_{uuid.uuid4().hex[:6]}"
            if not batch_data.get("created_at"):
                batch_data["created_at"] = datetime.now().strftime("%d/%m/%Y %H:%M:%S")

            # Prepend to history (newest first)
            self.history.insert(0, batch_data)
            
            # Keep up to 200 most recent records
            if len(self.history) > 200:
                self.history = self.history[:200]

            self._save_history()
            return batch_data

    def get_history(self, limit: int = 100) -> list:
        """Returns list of past exam generation batches."""
        with self._lock:
            return list(self.history[:limit])

    def get_latest_batch(self) -> Optional[dict]:
        """Returns the most recent exam generation batch, or None."""
        with self._lock:
            return self.history[0] if self.history else None

    def get_batch(self, batch_id: str) -> Optional[dict]:
        """Finds a specific batch by its batch_id."""
        with self._lock:
            for b in self.history:
                if b.get("batch_id") == batch_id:
                    return dict(b)
            return None

    def delete_batch(self, batch_id: str) -> bool:
        """Removes a batch from history."""
        with self._lock:
            initial_len = len(self.history)
            self.history = [b for b in self.history if b.get("batch_id") != batch_id]
            if len(self.history) < initial_len:
                self._save_history()
                return True
            return False

    def clear_all(self):
        """Clears all exam history."""
        with self._lock:
            self.history = []
            self._save_history()
