#pragma once
#include "PSSGNode.h"
#include <string>

class PSSGParser {
public:
    explicit PSSGParser(const std::string &path);
    PSSGNode parse();
private:
    std::string path;
};
