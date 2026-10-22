using Quantum;
using UnityEngine;

namespace Pong
{
    /// <summary>
    /// Renders the Pong simulation with plain sprites. Reads the predicted frame every render
    /// frame, so no entity prefabs or Quantum view assets are needed.
    /// </summary>
    public unsafe class PongView : MonoBehaviour
    {
        private const float HalfWidth = 8f;
        private const float HalfHeight = 4.5f;
        private const float PaddleX = 7f;

        private Transform _left, _right, _ball;
        private Camera _camera;

        private void Awake()
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);

            _left = CreateSprite("Left Paddle", sprite, new Vector2(0.5f, 2f), new Color(0.3f, 0.8f, 1f));
            _right = CreateSprite("Right Paddle", sprite, new Vector2(0.5f, 2f), new Color(1f, 0.6f, 0.3f));
            _ball = CreateSprite("Ball", sprite, new Vector2(0.5f, 0.5f), Color.white);
            CreateSprite("Center Line", sprite, new Vector2(0.05f, HalfHeight * 2f), new Color(1, 1, 1, 0.25f));

            _camera = Camera.main;
            if (_camera == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                _camera = go.AddComponent<Camera>();
            }
            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.05f, 0.06f, 0.1f);
            _camera.transform.position = new Vector3(0, 0, -10);
        }

        private Transform CreateSprite(string objectName, Sprite sprite, Vector2 size, Color color)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            go.transform.localScale = new Vector3(size.x, size.y, 1);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            return go.transform;
        }

        private void LateUpdate()
        {
            // Fit the 16x9 field into any aspect ratio.
            _camera.orthographicSize = Mathf.Max(HalfHeight + 0.5f, (HalfWidth + 0.5f) / _camera.aspect);

            var frame = PongGame.PredictedFrame();
            if (frame == null) return;

            var g = frame.Global;
            _left.position = new Vector3(-PaddleX, g->LeftY.AsFloat, 0);
            _right.position = new Vector3(PaddleX, g->RightY.AsFloat, 0);
            _ball.position = new Vector3(g->BallPosition.X.AsFloat, g->BallPosition.Y.AsFloat, 0);
        }
    }
}
