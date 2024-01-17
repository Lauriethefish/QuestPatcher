# Reformats the attribute resource IDs in the Android source code into a format that's easier to load by QuestPatcher.Axml
# The resource IDs are found in source here: https://android.googlesource.com/platform/frameworks/base/+/refs/heads/main/core/api/current.txt
# This file should be named `current.txt` and placed in the same directory as this script.
# The reformatted resource IDs are then written out to ./Resources/resourceIds.bin


resource_ids = {}
with open("current.txt", "r") as file:
    reading_attrs = False
    for line in file:
        if "R.attr" in line:
            reading_attrs = True
        elif "}" in line:
            # Stop once we've reached the end of the attributes
            if reading_attrs:
                break
        else:
            # NB this parsing code is fairly fragile, may break due to formatting issues, but it works on the current Android source

            start_name = line.find("int ")
            if start_name == -1:
                continue

            end_name = line.find(" = ", start_name + 4)
            end_id = line.find(";", end_name)

            name = line[start_name + 4:end_name]
            resource_id = int(line[end_name + 3:end_id])

            resource_ids[name] = resource_id

with open("./Resources/resourceIds.bin", "wb") as out_file:
    for name, id in resource_ids.items():
        name_bytes = name.encode(encoding="utf-16-le") # Use -le to disable BOM
        out_file.write(len(name_bytes).to_bytes(4, byteorder='little', signed=False))
        out_file.write(name_bytes)
        out_file.write(id.to_bytes(4, byteorder='little', signed=False))
