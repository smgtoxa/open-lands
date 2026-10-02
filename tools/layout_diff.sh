#!/bin/bash
# Compares element boxes: tools/layout_web.py dump (web) vs the Unity autopilot dump:.
#   bash tools/layout_diff.sh WEB.txt UNITY.txt [tolerance px]
# Prints, in document order, elements whose box differs (web -> unity), and elements only on one side.
# classes that come and go with combat and timing (hit flashes, threat, low health) are not part of the key
norm() { sed -E 's/\.(hit|threatened|low-hp|low-mp|busy|frozen|poisoned|striking|last|targeting|drop)([.[ >])/\2/g; s/\.(hit|threatened|low-hp|low-mp|busy|frozen|poisoned|striking|last|targeting|drop)([.[ >])/\2/g' "$1"; }
awk -v tol="${3:-2}" '
  function abs(v) { return v < 0 ? -v : v }
  NR == FNR { w[$1] = $2 " " $3 " " $4 " " $5; order[++n] = $1; next }
  { u[$1] = $2 " " $3 " " $4 " " $5 }
  END {
    for (i = 1; i <= n; i++) {
      k = order[i]
      if (!(k in u)) { print "only-web  " k "  " w[k]; continue }
      split(w[k], a, " "); split(u[k], b, " ")
      if (abs(a[1]-b[1]) > tol || abs(a[2]-b[2]) > tol || abs(a[3]-b[3]) > tol || abs(a[4]-b[4]) > tol)
        printf "diff  %s  web %s  unity %s\n", k, w[k], u[k]
    }
    for (k in u) if (!(k in w)) print "only-unity  " k "  " u[k]
  }' <(norm "$1") <(norm "$2")
