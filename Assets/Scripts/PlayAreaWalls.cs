using UnityEngine;

/// <summary>
/// Fades each of the 4 play-area walls toward invisible whenever it actually
/// stands between the camera and the play area, so the walls read as an
/// enclosing office room without ever blocking the isometric view of the
/// devices inside. Built once by GameBootstrap.SetupWalls(); purely cosmetic —
/// no collider, no gameplay effect, so it never needs to register with
/// DragDropManager's interaction-mode gate.
/// </summary>
public class PlayAreaWalls : MonoBehaviour
{
    struct Wall
    {
        public Renderer renderer;
        public Vector3 outwardNormal;
        public float height; // rest height above the floor
        public Color baseColor;
        public MaterialPropertyBlock block;
    }

    // A wall can only hide part of the room when the camera is on its outer
    // side — from inside the room (zoomed in, looking at a wall up close) it
    // stays fully opaque. This deliberately isn't keyed off the camera's
    // direction from the room's center: zoomed in, the camera orbits close to
    // the center, so that direction swings with every pan and used to fade
    // out the very wall being looked at.
    //
    // Once the camera is outside, the wall fades by how wide a strip of floor
    // it hides behind itself: a camera high above or barely past the wall
    // hides almost nothing, so the wall fades in gradually as Q/E rotation
    // swings the orbit across its plane instead of popping; a camera down at
    // wall-top height hides the whole room, so the wall vanishes outright.
    const float FullFadeHiddenWidth = 0.5f; // world units of hidden floor at which a wall is fully invisible

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    float floorY;
    Wall[] walls;

    // Must run before GameBootstrap's wall grow-in animation shrinks the walls,
    // so each wall's height is captured at its rest size.
    public void Init(Vector3 areaCenter, params (Renderer renderer, Vector3 outwardNormal)[] wallDefs)
    {
        floorY = areaCenter.y;
        walls = new Wall[wallDefs.Length];
        for (int i = 0; i < wallDefs.Length; i++)
        {
            walls[i] = new Wall
            {
                renderer      = wallDefs[i].renderer,
                outwardNormal = wallDefs[i].outwardNormal,
                height        = wallDefs[i].renderer.bounds.max.y - floorY,
                baseColor     = wallDefs[i].renderer.sharedMaterial.color,
                block         = new MaterialPropertyBlock(),
            };
        }
    }

    // LateUpdate so the fade reacts to IsometricCamera's own lerped
    // position for the current frame rather than lagging a frame behind it.
    void LateUpdate()
    {
        if (walls == null) return;
        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 camPos    = cam.transform.position;
        float   camHeight = camPos.y - floorY;

        for (int i = 0; i < walls.Length; i++)
        {
            // How far past the wall's plane the camera sits, horizontally
            // (outward normals are horizontal); <= 0 means inside the room.
            float beyond = Vector3.Dot(camPos - walls[i].renderer.transform.position, walls[i].outwardNormal);
            float alpha  = 1f;
            if (beyond > 0f)
            {
                // Similar triangles over the wall's top edge give the width
                // of floor hidden behind it; at or below wall-top height the
                // camera can't see over it at all.
                float headroom = camHeight - walls[i].height;
                float hidden   = headroom > 0f ? walls[i].height * beyond / headroom : float.PositiveInfinity;
                alpha = 1f - Mathf.Clamp01(hidden / FullFadeHiddenWidth);
            }

            Color c = walls[i].baseColor;
            walls[i].block.SetColor(BaseColorId, new Color(c.r, c.g, c.b, alpha));
            walls[i].renderer.SetPropertyBlock(walls[i].block);
        }
    }
}
