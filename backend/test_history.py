import unittest
import os
import shutil
import tempfile
from fastapi.testclient import TestClient
from history_manager import HistoryManager
from app import app, hm

class TestHistoryManager(unittest.TestCase):
    def setUp(self):
        self.test_dir = tempfile.mkdtemp()
        self.hm = HistoryManager(config_dir=self.test_dir)
        self.client = TestClient(app)

    def tearDown(self):
        shutil.rmtree(self.test_dir, ignore_errors=True)

    def test_add_and_get_history(self):
        batch = {
            "exam_date": "26/08/2026",
            "candidate_count": 2,
            "departments": ["Khoa Ngoại tổng hợp"],
            "zip_filename": "test.zip",
            "zip_url": "/api/exams/download/file/test.zip",
            "candidates": [
                {"name": "Thí sinh 1", "id": "SBD01", "docx_filename": "test1.docx", "docx_url": "/api/exams/download/file/test1.docx"}
            ]
        }
        added = self.hm.add_batch(batch)
        self.assertTrue(added["batch_id"].startswith("batch_"))
        self.assertIsNotNone(added["created_at"])

        history = self.hm.get_history()
        self.assertEqual(len(history), 1)
        self.assertEqual(history[0]["batch_id"], added["batch_id"])

        latest = self.hm.get_latest_batch()
        self.assertIsNotNone(latest)
        self.assertEqual(latest["batch_id"], added["batch_id"])

    def test_delete_batch(self):
        batch = {"exam_date": "26/08/2026", "candidate_count": 1}
        added = self.hm.add_batch(batch)
        self.assertEqual(len(self.hm.get_history()), 1)

        deleted = self.hm.delete_batch(added["batch_id"])
        self.assertTrue(deleted)
        self.assertEqual(len(self.hm.get_history()), 0)

    def test_api_history_endpoints(self):
        # 1. GET /api/exams/history
        r = self.client.get("/api/exams/history")
        self.assertEqual(r.status_code, 200)
        self.assertIsInstance(r.json(), list)

        # 2. GET /api/exams/latest
        r_latest = self.client.get("/api/exams/latest")
        self.assertEqual(r_latest.status_code, 200)
        self.assertIn("batch", r_latest.json())

if __name__ == "__main__":
    unittest.main()
