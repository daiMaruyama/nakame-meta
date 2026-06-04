using UnityEngine;

namespace NakameMeta.City
{
    /// <summary>
    /// Geometry-derived classification of a single PLATEAU building.
    /// Attached to every generated building so a later pass can re-tag by
    /// usage (matching PLATEAU building UID) and overwrite <see cref="category"/>.
    /// </summary>
    public enum BuildingCategory
    {
        LowResidential,   // 低層住宅
        MidRise,          // 中層マンション
        OfficeCommercial, // オフィス / 商業ビル
        Zakkyo            // 雑居ビル（中層・小フットプリント）
    }

    public sealed class BuildingInfo : MonoBehaviour
    {
        [Tooltip("PLATEAU building UID (e.g. 13110/2023/.../bldg_xxxx). Join key for later usage tagging.")]
        public string uid;

        [Tooltip("Category assigned from geometry (height + footprint). Overwritable later by usage data.")]
        public BuildingCategory category;

        [Tooltip("Building height in metres (mesh Y extent).")]
        public float heightMeters;

        [Tooltip("Footprint area in m^2 (XZ bounding box).")]
        public float footprintArea;
    }
}
