#pragma once
#include "PSSGNode.h"
#include <string>

class PSSGWriter {
public:
    explicit PSSGWriter(const PSSGNode &root);
    void save(const std::string &path);
private:
    PSSGNode root;
    void computeSizes(PSSGNode &node);
    void writeNode(std::ofstream &out, const PSSGNode &node);
};
