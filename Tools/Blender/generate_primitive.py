import bpy
import sys
import os

def clear_scene():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)

def parse_args():
    argv = sys.argv
    if "--" not in argv:
        return {"type": "cube", "name": "Primitive", "scale": (1,1,1), "output": "output.fbx"}
    argv = argv[argv.index("--") + 1:]
    args = {"type": "cube", "name": "Primitive", "scale": (1,1,1), "output": "output.fbx"}
    i = 0
    while i < len(argv):
        if argv[i] == "--type": args["type"] = argv[i+1]; i += 2
        elif argv[i] == "--name": args["name"] = argv[i+1]; i += 2
        elif argv[i] == "--scale": args["scale"] = (float(argv[i+1]), float(argv[i+2]), float(argv[i+3])); i += 4
        elif argv[i] == "--output": args["output"] = argv[i+1]; i += 2
        else: i += 1
    return args

def create_object(obj_type, name, scale):
    if obj_type == "cube": bpy.ops.mesh.primitive_cube_add()
    elif obj_type == "cylinder": bpy.ops.mesh.primitive_cylinder_add()
    elif obj_type == "sphere": bpy.ops.mesh.primitive_uv_sphere_add()
    elif obj_type == "plane": bpy.ops.mesh.primitive_plane_add()
    else: bpy.ops.mesh.primitive_cube_add()
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(scale=True)
    return obj

def export_fbx(filepath):
    os.makedirs(os.path.dirname(os.path.abspath(filepath)), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=os.path.abspath(filepath), use_selection=True, apply_scale_options='FBX_SCALE_ALL', path_mode='COPY', embed_textures=True)
    print(f"[ShelfLife] Exported: {os.path.abspath(filepath)}")

if __name__ == "__main__":
    args = parse_args()
    clear_scene()
    obj = create_object(args["type"], args["name"], args["scale"])
    obj.select_set(True)
    export_fbx(args["output"])
    print(f"[ShelfLife] Done: {args['name']} ({args['type']}) -> {args['output']}")
