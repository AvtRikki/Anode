#!/usr/bin/env bash
# Downloads KiCad demo/QA files used by the test suite into test-data/kicad/.
# Files come from the KiCad repository pinned to a commit; they are not committed here (licenses vary).
set -euo pipefail

KICAD_COMMIT="${KICAD_COMMIT:-a46f62841996fbc9edac9b9daef348651a1b1496}"
BASE="https://gitlab.com/kicad/code/kicad/-/raw/${KICAD_COMMIT}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DEST="${ROOT}/test-data/kicad"

FILES=(
  demos/cm5_minima/CM5_MINIMA_3.kicad_pcb
  demos/complex_hierarchy/complex_hierarchy.kicad_pcb
  demos/constraints/constraints.kicad_pcb
  demos/ecc83/ecc83-pp.kicad_pcb
  demos/ecc83/ecc83-pp_v2.kicad_pcb
  demos/interf_u/interf_u.kicad_pcb
  demos/jetson-agx-thor-baseboard/jetson-agx-thor-baseboard.kicad_pcb
  demos/kit-dev-coldfire-xilinx_5213/kit-dev-coldfire-xilinx_5213.kicad_pcb
  demos/microwave/microwave.kicad_pcb
  demos/multichannel/multichannel_mixer.kicad_pcb
  demos/openair-max/One-Air-Max.kicad_pcb
  demos/pic_programmer/pic_programmer.kicad_pcb
  demos/royalblue54L_feather/RoyalBlue54L-Feather.kicad_pcb
  "demos/sonde xilinx/sonde xilinx.kicad_pcb"
  demos/stickhub/StickHub.kicad_pcb
  demos/tiny_tapeout/tinytapeout-demo.kicad_pcb
  demos/video/video.kicad_pcb
  demos/vme-wren/vme-wren.kicad_pcb
  qa/data/pcbnew/api_kitchen_sink.kicad_pcb
  qa/data/pcbnew/custom_pads.kicad_pcb
  qa/data/pcbnew/footprints_load_save.kicad_pcb
  qa/data/pcbnew/graphics_load_save_v20240108.kicad_pcb
  qa/data/pcbnew/groups_load_save.kicad_pcb
  qa/data/pcbnew/embedded_table_rotated_legacy_v10.kicad_pcb
  qa/data/pcbnew/intersectingzones.kicad_pcb
  qa/data/pcbnew/ipc2581/thirty-copper-layers.kicad_pcb
  qa/data/pcbnew/issue20249/dimension.kicad_pcb
  qa/data/pcbnew/custom_fields.kicad_pcb
  demos/cm5_minima/CM5.kicad_sch
  demos/cm5_minima/CM5IO.kicad_sym
  demos/complex_hierarchy/complex_hierarchy.kicad_sch
  demos/complex_hierarchy/complex_hierarchy.kicad_pro
  demos/complex_hierarchy/ampli_ht.kicad_sch
  qa/data/eeschema/netlists/test_multiunit_reannotate_5/test_multiunit_reannotate_5.kicad_sch
  qa/data/eeschema/netlists/test_multiunit_reannotate_5/test_multiunit_reannotate_5.kicad_pro
  demos/pic_programmer/pic_programmer.kicad_sch
  demos/video/video.kicad_sch
  demos/cm5_minima/CM5IO.pretty/C_0402_1005Metric.kicad_mod
  demos/cm5_minima/CM5IO.pretty/KiCad-Logo2_5mm_SilkScreen.kicad_mod
  demos/cm5_minima/CM5IO.pretty/L_Bourns_SRP5030CC.kicad_mod
)

mkdir -p "$DEST"
ok=0
failed=0
for f in "${FILES[@]}"; do
  out="${DEST}/${f}"
  if [[ -s "$out" ]]; then
    ok=$((ok + 1))
    continue
  fi
  mkdir -p "$(dirname "$out")"
  if curl -fsSL -o "$out" "${BASE}/${f// /%20}"; then
    ok=$((ok + 1))
  else
    rm -f "$out"
    echo "warning: failed to fetch $f" >&2
    failed=$((failed + 1))
  fi
done

echo "$KICAD_COMMIT" > "${DEST}/COMMIT"
echo "fixtures: ${ok} ok, ${failed} failed -> ${DEST}"
