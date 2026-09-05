# Data licence

The code in this repository is under the [MIT licence](LICENSE). The material data in
`data/glasses/` and the generated catalog in `src/Caustikon.Glasses/Generated/` are
derived from sources with their own terms, listed here.

## RefractiveIndex.INFO database

Every catalogued glass is taken from the RefractiveIndex.INFO database,
<https://github.com/polyanskiy/refractiveindex.info-database>, at the commit recorded
in `data/glasses/manifest.json`. Mikhail Polyanskiy released the database into the
public domain under the [CC0 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/)
dedication: it may be used, modified and redistributed without restriction, including
commercially, and no permission is required. Attribution is not a condition of the
licence. This project attributes anyway, because a physical constant without a
citation is not usable evidence.

Reference: M. N. Polyanskiy, "Refractiveindex.info database of optical constants",
*Scientific Data* 11, 94 (2024), <https://doi.org/10.1038/s41597-023-02898-2>.

Each entry's provenance names the manufacturer catalog the database transcribed
(for example "SCHOTT Zemax catalog 2017-01-20b"), the path of the entry inside the
database, and the date the database was read. The manufacturer catalogs are the
authority for the printed `nd` and `Vd` values the tests verify against.

## CIE 1931 colour matching functions and illuminant D65

`data/colour/` holds the CIE 1931 2° standard observer at 1 nm and the relative
spectral power of CIE standard illuminant D65 at 5 nm. The numeric tables are those
published by the Commission Internationale de l'Éclairage; the copies here were
extracted from the data files of the colour-science project,
<https://github.com/colour-science/colour>, distributed under the BSD-3-Clause
licence (Copyright 2013 Colour Developers). The tables are reproduced unchanged.

## What is not covered

Manufacturer trademarks (SCHOTT, OHARA, HOYA, CDGM, HIKARI, SUMITA and others) belong
to their owners. Naming a glass by its catalog name is a factual reference, not an
endorsement by the manufacturer.
