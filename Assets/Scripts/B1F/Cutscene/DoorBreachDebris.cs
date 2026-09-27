using System.Collections.Generic;
using UnityEngine;

namespace DeFrag.B1F
{
    /// <summary>
    /// Local physics debris for the door breach, built from the fractured door's own cell meshes.
    /// Pieces ignore player colliders and are frozen as static decoration once the shot is over,
    /// so they never affect gameplay or navigation.
    /// </summary>
    public sealed class DoorBreachDebris : MonoBehaviour
    {
        [Tooltip("파손 문 루트. 자식 셀 메시를 파편 모양으로 사용합니다.")]
        [SerializeField] private Transform fragmentSource;
        [SerializeField, Min(0)] private int pieceCount = 38;
        [Tooltip("파편 한 조각의 최대 변 길이 (m).")]
        [SerializeField] private Vector2 pieceSize = new(0.18f, 0.6f);
        [SerializeField] private Vector2 speedRange = new(4f, 10.5f);
        [SerializeField, Range(0f, 80f)] private float spreadAngle = 38f;
        [SerializeField, Min(0f)] private float upwardBias = 0.25f;
        [Tooltip("문 구멍 크기 (폭, 높이)와 바닥으로부터의 시작 높이.")]
        [SerializeField] private Vector2 openingSize = new(2.2f, 2.6f);
        [SerializeField, Min(0f)] private float openingBottom = 0.3f;
        [SerializeField, Min(0.01f)] private float density = 60f;

        private readonly List<Rigidbody> pieces = new();
        private readonly List<(Mesh mesh, Material material)> shapes = new();

        private void CollectShapes()
        {
            shapes.Clear();
            if (fragmentSource == null) return;
            foreach (MeshFilter filter in fragmentSource.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || !filter.name.Contains("cell")) continue;
                Vector3 size = filter.sharedMesh.bounds.size;
                if (Mathf.Max(size.x, size.y, size.z) < 0.08f) continue;
                var renderer = filter.GetComponent<MeshRenderer>();
                shapes.Add((filter.sharedMesh, renderer != null ? renderer.sharedMaterial : null));
            }
        }

        /// <summary>Blow pieces out of the opening along the door's forward (into the room).</summary>
        public void Burst(Transform door, Vector3 intoRoom, IReadOnlyList<Collider> ignore)
        {
            if (shapes.Count == 0) CollectShapes();
            if (shapes.Count == 0 || door == null) return;
            Vector3 across = Vector3.Cross(Vector3.up, intoRoom).normalized;
            for (int i = 0; i < pieceCount; i++)
            {
                var (mesh, material) = shapes[Random.Range(0, shapes.Count)];
                var piece = new GameObject($"Breach Debris {i}");
                piece.transform.SetParent(transform, true);
                Vector3 offset = across * Random.Range(-0.5f, 0.5f) * openingSize.x +
                                 Vector3.up * (openingBottom + Random.value * openingSize.y);
                piece.transform.SetPositionAndRotation(door.position + offset + intoRoom * 0.2f, Random.rotation);
                Vector3 meshSize = mesh.bounds.size;
                float longest = Mathf.Max(0.01f, Mathf.Max(meshSize.x, meshSize.y, meshSize.z));
                piece.transform.localScale = Vector3.one * (Random.Range(pieceSize.x, pieceSize.y) / longest);
                piece.AddComponent<MeshFilter>().sharedMesh = mesh;
                piece.AddComponent<MeshRenderer>().sharedMaterial = material;
                var box = piece.AddComponent<BoxCollider>();
                if (ignore != null) foreach (Collider other in ignore) if (other != null) Physics.IgnoreCollision(box, other);

                var body = piece.AddComponent<Rigidbody>();
                Vector3 extents = Vector3.Scale(mesh.bounds.size, piece.transform.localScale);
                body.mass = Mathf.Max(0.2f, extents.x * extents.y * extents.z * density);
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                Vector3 direction = Quaternion.AngleAxis(Random.Range(-spreadAngle, spreadAngle), Vector3.up) *
                                    Quaternion.AngleAxis(-Random.Range(0f, spreadAngle * 0.6f), across) * intoRoom;
                direction = (direction + Vector3.up * upwardBias * Random.value).normalized;
                body.linearVelocity = direction * Random.Range(speedRange.x, speedRange.y);
                body.angularVelocity = Random.insideUnitSphere * 18f;
                pieces.Add(body);
            }
        }

        /// <summary>Leave the debris lying where it landed, without physics or collision.</summary>
        public void Freeze()
        {
            foreach (Rigidbody body in pieces)
            {
                if (body == null) continue;
                body.isKinematic = true;
                if (body.TryGetComponent(out Collider collider)) collider.enabled = false;
            }
        }

        public void Clear()
        {
            foreach (Rigidbody body in pieces) if (body != null) Destroy(body.gameObject);
            pieces.Clear();
        }
    }
}
